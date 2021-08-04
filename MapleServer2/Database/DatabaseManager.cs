﻿using System;
using System.Collections.Generic;
using System.Linq;
using Maple2Storage.Types;
using MapleServer2.Database.Types;
using MapleServer2.Types;
using Microsoft.EntityFrameworkCore;

namespace MapleServer2.Database
{
    public static class DatabaseManager
    {
        public static bool Insert(dynamic entry)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(entry).State = EntityState.Added;
                return SaveChanges(context);
            }
        }

        public static bool Update(dynamic entry)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(entry).State = EntityState.Modified;
                return SaveChanges(context);
            }
        }

        public static bool Delete(dynamic entry)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(entry).State = EntityState.Deleted;
                return SaveChanges(context);
            }
        }

        public static long CreateAccount(Account account)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Accounts.Add(account);
                SaveChanges(context);
                return account.Id;
            }
        }

        public static Account GetAccount(string username, string passwordHash)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Accounts.FirstOrDefault(a => a.Username == username && a.PasswordHash == passwordHash);
            }
        }

        public static Account GetAccount(long accountId)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Accounts
                .Include(p => p.Home)
                .FirstOrDefault(a => a.Id == accountId);
            }
        }

        public static bool Authenticate(string username, string password, out Account account)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Account dbAccount = context.Accounts.SingleOrDefault(x => x.Username == username);
                if (BCrypt.Net.BCrypt.Verify(password, dbAccount.PasswordHash))
                {
                    account = dbAccount;
                    return true;
                }

                account = null;
                return false;
            }
        }

        public static bool AccountExists(string username)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Accounts.Any(a => a.Username == username);
            }
        }

        public static List<Player> GetAccountCharacters(long accountId)
        {
            List<Player> characters;
            using (DatabaseContext context = new DatabaseContext())
            {
                characters = context.Characters
                .Where(p => p.AccountId == accountId && !p.IsDeleted)
                .Include(p => p.Levels)
                .Include(p => p.Inventory).ThenInclude(p => p.DB_Items)
                .ToList();
            }

            foreach (Player player in characters)
            {
                player.Inventory = new Inventory(player.Inventory);
                Levels levels = player.Levels;
                player.Levels = new Levels(player, levels.Level, levels.Exp, levels.RestExp, levels.PrestigeLevel, levels.PrestigeExp, levels.MasteryExp, levels.Id);
            }

            return characters;
        }

        public static long CreateCharacter(Player player)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Characters.Add(player);
                SaveChanges(context);
                return player.CharacterId;
            }
        }

        public static Player GetCharacter(long characterId)
        {
            Player player;
            List<Mail> mails = new List<Mail>();
            using (DatabaseContext context = new DatabaseContext())
            {
                player = context.Characters
                .Include(p => p.Levels)
                .Include(p => p.SkillTabs)
                .Include(p => p.Guild)
                .Include(p => p.GameOptions).ThenInclude(p => p.Hotbars)
                .Include(p => p.Wallet)
                .Include(p => p.BuddyList)
                .Include(p => p.Trophies)
                .Include(p => p.Inventory).ThenInclude(p => p.DB_Items)
                .Include(p => p.BankInventory).ThenInclude(p => p.DB_Items)
                .FirstOrDefault(p => p.CharacterId == characterId);
                if (player == null)
                {
                    return null;
                }
                player.Account = GetAccount(player.AccountId);
                mails = context.Mails.Where(m => m.PlayerId == characterId).ToList();

                player.QuestList = context.Quests.Where(x => x.Player.CharacterId == characterId).ToList();
            }

            Levels levels = player.Levels;
            Wallet wallet = player.Wallet;
            foreach (Trophy trophy in player.Trophies)
            {
                player.TrophyData[trophy.Id] = trophy;
            }
            foreach (QuestStatus item in player.QuestList)
            {
                item.SetMetadataValues(item.Id);
            }
            player.Inventory = new Inventory(player.Inventory);
            player.BankInventory = new BankInventory(player.BankInventory);
            player.Mailbox = new Mailbox(mails);
            player.SkillTabs.ForEach(skilltab => skilltab.GenerateSkills(player.Job));
            player.Levels = new Levels(player, levels.Level, levels.Exp, levels.RestExp, levels.PrestigeLevel, levels.PrestigeExp, levels.MasteryExp, levels.Id);

            return player;
        }

        public static void UpdateCharacter(Player player)
        {
            player.TrophyCount = new int[3];
            if (player.Trophies != null)
            {
                List<List<long>> combat = player.Trophies.Where(x => x.Type == "combat").Select(x => x.Timestamps).ToList();
                foreach (List<long> item in combat.Where(x => x.Count != 0))
                {
                    player.TrophyCount[0] += item.Count;
                }

                List<List<long>> adventure = player.Trophies.Where(x => x.Type == "adventure").Select(x => x.Timestamps).ToList();
                foreach (List<long> item in adventure.Where(x => x.Count != 0))
                {
                    player.TrophyCount[1] += item.Count;
                }

                List<List<long>> living = player.Trophies.Where(x => x.Type == "living").Select(x => x.Timestamps).ToList();
                foreach (List<long> item in living.Where(x => x.Count != 0))
                {
                    player.TrophyCount[2] += item.Count;
                }
            }

            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(player).State = EntityState.Modified;
                context.Entry(player.Wallet).State = EntityState.Modified;
                context.Entry(player.Levels).State = EntityState.Modified;
                context.Entry(player.Inventory).State = EntityState.Modified;
                context.Entry(player.BankInventory).State = EntityState.Modified;
                context.Entry(player.GameOptions).State = EntityState.Modified;

                if (player.GuildMember != null)
                {
                    context.Entry(player.GuildMember).State = EntityState.Modified;
                }
                if (player.Account != null)
                {
                    context.Entry(player.Account).State = EntityState.Modified;
                }
                UpdateHotbars(player);
                UpdateQuests(player);
                UpdateTrophies(player);
                UpdateItems(player);
                SaveChanges(context);
            }
        }

        public static bool DeleteCharacter(long characterId)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Player character = context.Characters.Find(characterId);
                character.IsDeleted = true;

                return SaveChanges(context);
            }
        }

        public static bool NameExists(string name)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Characters.FirstOrDefault(x => x.Name.ToLower() == name.ToLower()) != null;
            }
        }

        private static void UpdateHotbars(Player player)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (Hotbar hotbar in player.GameOptions.Hotbars)
                {
                    Hotbar dbHotbar = context.Hotbars.Find(hotbar.Id);
                    context.Entry(dbHotbar).CurrentValues.SetValues(hotbar);
                }
                context.SaveChanges();
            }
        }

        public static long AddSkillTab(SkillTab skillTab)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Player dbPlayer = context.Characters.Find(skillTab.Player.CharacterId);
                skillTab.Player = dbPlayer;
                context.SkillTabs.Add(skillTab);
                SaveChanges(context);
                return skillTab.Uid;
            }
        }

        public static bool UpdateSkillTabs(Player player)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (SkillTab skillTab in player.SkillTabs)
                {
                    SkillTab dbSkillTab = context.SkillTabs.Find(skillTab.Uid);
                    context.Entry(dbSkillTab).CurrentValues.SetValues(skillTab);
                }
                return SaveChanges(context);
            }
        }

        public static long AddItem(Item item)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Items.Add(item);
                SaveChanges(context);
                return item.Uid;
            }
        }

        public static Item GetItem(long uid)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Items.FirstOrDefault(x => x.Uid == uid);
            }
        }

        public static void UpdateItems(Player player)
        {
            Inventory inventory = player.Inventory;
            inventory.DB_Items = inventory.Items.Values.Where(x => x != null).ToList();
            inventory.DB_Items.AddRange(inventory.Equips.Values.Where(x => x != null).ToList());
            inventory.DB_Items.AddRange(inventory.Cosmetics.Values.Where(x => x != null).ToList());

            BankInventory bankInventory = player.BankInventory;
            bankInventory.DB_Items = bankInventory.Items.Where(x => x != null).ToList();

            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (Item item in inventory.DB_Items)
                {
                    Item dbItem = context.Items.Find(item.Uid);
                    if (dbItem == null)
                    {
                        item.Inventory = inventory;
                        context.Entry(item).State = EntityState.Added;
                        continue;
                    }
                    item.BankInventory = null;
                    dbItem.BankInventory = null;
                    context.Entry(dbItem).CurrentValues.SetValues(item);
                }

                foreach (Item item in bankInventory.DB_Items)
                {
                    Item dbItem = context.Items.Find(item.Uid);
                    if (dbItem == null)
                    {
                        item.BankInventory = bankInventory;
                        context.Entry(item).State = EntityState.Added;
                        continue;
                    }
                    item.Inventory = null;
                    dbItem.Inventory = null;
                    context.Entry(dbItem).CurrentValues.SetValues(item);
                }
                SaveChanges(context);
            }
        }

        public static long AddTrophy(Trophy trophy)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(trophy).State = EntityState.Added;
                SaveChanges(context);
                return trophy.Uid;
            }
        }

        public static void UpdateTrophies(Player player)
        {
            player.Trophies = player.TrophyData.Values.ToList();
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (Trophy trophy in player.Trophies)
                {
                    Trophy dbTrophy = context.Trophies.Find(trophy.Uid);
                    if (dbTrophy == null)
                    {
                        context.Entry(trophy).State = EntityState.Added;
                        continue;
                    }
                    context.Entry(dbTrophy).CurrentValues.SetValues(trophy);
                }
                SaveChanges(context);
            }
        }

        public static List<Guild> GetGuilds()
        {
            List<Guild> guilds = new List<Guild>();
            using (DatabaseContext context = new DatabaseContext())
            {
                guilds = context.Guilds.Include(x => x.Leader).Include(x => x.Members).ToList();
                foreach (Guild guild in guilds)
                {
                    guild.Leader = context.Characters
                    .Include(x => x.Account).ThenInclude(x => x.Home)
                    .Include(x => x.Levels)
                    .FirstOrDefault(x => x.CharacterId == guild.Leader.CharacterId);
                    foreach (GuildMember guildMember in guild.Members)
                    {
                        guildMember.Player = context.Characters
                        .Include(x => x.Account).ThenInclude(x => x.Home)
                        .Include(x => x.Levels)
                        .FirstOrDefault(x => x.CharacterId == guildMember.Id);
                    }
                }
            }
            return guilds;
        }

        public static bool GuildExists(string guildName)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Guild result = context.Guilds.FirstOrDefault(p => p.Name.ToLower() == guildName.ToLower());

                return result != null;
            }
        }

        public static long CreateGuild(Guild guild)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(guild).State = EntityState.Added;
                SaveChanges(context);
                return guild.Id;
            }
        }

        public static bool UpdateGuild(Guild guild)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Guild dbGuild = context.Guilds.AsNoTracking().Include(x => x.Leader).FirstOrDefault(x => x.Id == guild.Id);

                context.Entry(guild).State = EntityState.Modified;
                if (dbGuild.Leader.CharacterId != guild.Leader.CharacterId)
                {
                    context.Entry(guild.Leader).State = EntityState.Modified;
                }

                return SaveChanges(context);
            }
        }

        public static bool CreateGuildMember(GuildMember member)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(member).State = EntityState.Added;
                return SaveChanges(context);
            }
        }

        public static bool CreateGuildApplication(GuildApplication application)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.GuildApplications.Add(application);
                return SaveChanges(context);
            }
        }

        public static void InsertShops(List<Shop> shops)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (Shop shop in shops)
                {
                    context.Shops.Add(shop);
                }
                SaveChanges(context);
            }
        }

        public static Shop GetShop(int id)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Shops.Include(x => x.Items).FirstOrDefault(x => x.Id == id);
            }
        }

        public static ShopItem GetShopItem(int uid)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.ShopItems.FirstOrDefault(x => x.Uid == uid);
            }
        }

        public static void InsertMeretMarketItems(List<MeretMarketItem> items)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (MeretMarketItem item in items)
                {
                    context.MeretMarketItems.Add(item);
                }
                SaveChanges(context);
            }
        }

        public static List<MeretMarketItem> GetMeretMarketItemsByCategory(MeretMarketCategory category)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.MeretMarketItems.Where(x => x.Category == category)
                    .Include(x => x.AdditionalQuantities)
                    .Include(x => x.Banner)
                    .ToList();
            }
        }

        public static MeretMarketItem GetMeretMarketItem(int id)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.MeretMarketItems
                    .Include(x => x.AdditionalQuantities)
                    .FirstOrDefault(x => x.MarketId == id);
            }
        }

        public static List<Banner> GetBanners()
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Banners.ToList();
            }
        }

        public static long AddQuest(QuestStatus questStatus)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(questStatus).State = EntityState.Added;
                SaveChanges(context);
                return questStatus.Uid;
            }
        }

        public static void UpdateQuests(Player player)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (QuestStatus quest in player.QuestList)
                {
                    QuestStatus dbQuest = context.Quests.Find(quest.Uid);
                    if (dbQuest == null)
                    {
                        context.Entry(quest).State = EntityState.Added;
                        continue;
                    }
                    context.Entry(dbQuest).CurrentValues.SetValues(quest);
                }
                SaveChanges(context);
            }
        }

        public static void InsertMapleopoly(List<MapleopolyTile> tiles)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (MapleopolyTile tile in tiles)
                {
                    context.MapleopolyTiles.Add(tile);
                }
                SaveChanges(context);
            }
        }

        public static List<MapleopolyTile> GetMapleopolyTiles()
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.MapleopolyTiles.OrderBy(x => x.TilePosition).ToList();
            }
        }

        public static MapleopolyTile GetsingleMapleopolyTile(int tilePosition)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.MapleopolyTiles.FirstOrDefault(x => x.TilePosition == tilePosition);
            }
        }

        public static void InsertGameEvents(List<GameEvent> gameEvents)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (GameEvent gameEvent in gameEvents)
                {
                    context.Events.Add(gameEvent);
                }
                SaveChanges(context);
            }
        }

        public static List<GameEvent> GetGameEvents()
        {
            List<GameEvent> gameEvents = new List<GameEvent>();
            using (DatabaseContext context = new DatabaseContext())
            {
                gameEvents = context.Events.Where(x => x.Active == true)
                    .Include(x => x.Mapleopoly)
                    .Include(x => x.StringBoard)
                    .Include(x => x.UGCMapContractSale)
                    .Include(x => x.UGCMapExtensionSale)
                    .Include(x => x.FieldPopupEvent)
                    .ToList();
            }
            return gameEvents;
        }

        public static GameEvent GetSingleGameEvent(GameEventType type)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.Events
                    .Include(x => x.UGCMapContractSale)
                    .Include(x => x.UGCMapExtensionSale)
                    .Include(x => x.FieldPopupEvent)
                    .FirstOrDefault(x => x.Type == type && x.Active == true);
            }
        }

        public static void InsertStringBoards(List<StringBoardEvent> stringBoards)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (StringBoardEvent stringBoard in stringBoards)
                {
                    context.Event_StringBoards.Add(stringBoard);
                }
                SaveChanges(context);
            }
        }

        public static void InsertMapleopolyEvent(List<MapleopolyEvent> mapleopolyEvents)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (MapleopolyEvent item in mapleopolyEvents)
                {
                    context.Event_Mapleopoly.Add(item);
                }
                SaveChanges(context);
            }
        }

        public static List<MapleopolyEvent> GetMapleopolyEvent()
        {
            List<MapleopolyEvent> mapleopolyEvents = new List<MapleopolyEvent>();
            using (DatabaseContext context = new DatabaseContext())
            {
                mapleopolyEvents = context.Event_Mapleopoly.ToList();
            }
            return mapleopolyEvents;
        }

        public static void InsertCardReverseGame(List<CardReverseGame> cards)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                foreach (CardReverseGame card in cards)
                {
                    context.CardReverseGame.Add(card);
                }
                SaveChanges(context);
            }
        }

        public static List<CardReverseGame> GetCardReverseGame()
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                return context.CardReverseGame.ToList();
            }
        }

        public static long CreateHouse(Home home)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(home).State = EntityState.Added;

                home.WarehouseItems = home.WarehouseInventory.Values.Where(x => x != null).ToList();
                foreach (Item item in home.WarehouseItems)
                {
                    context.Entry(item).State = EntityState.Modified;
                }

                home.FurnishingCubes = home.FurnishingInventory.Values.Where(x => x != null).ToList();
                foreach (Cube cube in home.FurnishingCubes)
                {
                    cube.Home = home;
                    context.Entry(cube).State = EntityState.Modified;
                }
                SaveChanges(context);
                return home.Id;
            }
        }

        public static Home GetHome(long id)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                Home home = context.Homes.Find(id);

                if (home != null)
                {
                    List<Cube> furnishingCubes = context.Cubes.Include(x => x.Item).Where(x => x.Home.Id == home.Id).ToList();
                    List<Item> warehouseItems = context.Items.Where(x => x.Home.Id == home.Id).ToList();
                    List<HomeLayout> layouts = context.HomeLayouts.Where(x => x.Home.Id == home.Id).ToList();
                    foreach (HomeLayout layout in layouts)
                    {
                        layout.Cubes = context.Cubes.Include(x => x.Item).Where(x => x.Layout.Id == layout.Id).ToList().ToList();
                    }
                    warehouseItems.ForEach(item => home.WarehouseInventory.Add(item.Uid, item));
                    furnishingCubes.ForEach(cube => home.FurnishingInventory.Add(cube.Uid, cube));

                    home.Layouts = layouts;
                    home.WarehouseItems = null;
                    home.FurnishingCubes = null;
                }

                return home;
            }
        }

        public static List<Home> GetHomesOnMap(int mapId)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                List<Home> homes = context.Homes.Where(x => x.MapId == mapId).ToList();

                foreach (Home home in homes)
                {
                    List<Cube> furnishingCubes = context.Cubes.Include(x => x.Item).Where(x => x.Home.Id == home.Id).ToList();
                    List<Item> warehouseItems = context.Items.Where(x => x.Home.Id == home.Id).ToList();
                    List<HomeLayout> layouts = context.HomeLayouts.Where(x => x.Home.Id == home.Id).ToList();
                    foreach (HomeLayout layout in layouts)
                    {
                        layout.Cubes = context.Cubes.Include(x => x.Item).Where(x => x.Layout.Id == layout.Id).ToList().ToList();
                    }
                    warehouseItems.ForEach(item => home.WarehouseInventory.Add(item.Uid, item));
                    furnishingCubes.ForEach(cube => home.FurnishingInventory.Add(cube.Uid, cube));

                    home.Layouts = layouts;
                    home.WarehouseItems = null;
                    home.FurnishingCubes = null;
                }

                return homes;
            }
        }

        public static void UpdateHome(Home home)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(home).State = EntityState.Modified;

                home.WarehouseItems = home.WarehouseInventory.Values.Where(x => x != null).ToList();
                foreach (Item item in home.WarehouseItems)
                {
                    item.Home = home;
                    context.Entry(item).State = EntityState.Modified;
                }

                home.FurnishingCubes = home.FurnishingInventory.Values.Where(x => x != null).ToList();
                foreach (Cube cube in home.FurnishingCubes)
                {
                    cube.Home = home;
                    context.Entry(cube).State = EntityState.Modified;
                }
                SaveChanges(context);
            }
        }

        public static long CreateCube(Cube cube)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(cube).State = EntityState.Added;
                SaveChanges(context);
                return cube.Uid;
            }
        }

        public static long AddLayout(HomeLayout homeLayout)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(homeLayout).State = EntityState.Added;
                SaveChanges(context);
                return homeLayout.Uid;
            }
        }

        public static bool DeleteLayout(HomeLayout homeLayout)
        {
            using (DatabaseContext context = new DatabaseContext())
            {
                context.Entry(homeLayout).State = EntityState.Deleted;
                homeLayout.Cubes.ForEach(cube => context.Entry(cube).State = EntityState.Deleted);
                return SaveChanges(context);
            }
        }

        private static bool SaveChanges(DatabaseContext context)
        {
            try
            {
                context.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }
    }
}
