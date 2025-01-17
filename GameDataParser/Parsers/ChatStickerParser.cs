﻿using System.Xml;
using GameDataParser.Files;
using Maple2.File.IO.Crypto.Common;
using Maple2Storage.Types;
using Maple2Storage.Types.Metadata;

namespace GameDataParser.Parsers;

public class ChatStickerParser : Exporter<List<ChatStickerMetadata>>
{
    public ChatStickerParser(MetadataResources resources) : base(resources, MetadataName.ChatSticker) { }

    protected override List<ChatStickerMetadata> Parse()
    {
        List<ChatStickerMetadata> chatStickers = new();
        foreach (PackFileEntry entry in Resources.XmlReader.Files)
        {
            if (!entry.Name.StartsWith("table/chatemoticon"))
            {
                continue;
            }

            XmlDocument document = Resources.XmlReader.GetXmlDocument(entry);
            XmlNodeList nodes = document.SelectNodes("/ms2/chatEmoticon");

            foreach (XmlNode node in nodes)
            {
                ChatStickerMetadata metadata = new()
                {
                    StickerId = int.Parse(node.Attributes["id"].Value),
                    GroupId = byte.Parse(node.Attributes["group_id"].Value),
                    CategoryId = short.Parse(node.Attributes["category_id"].Value)
                };

                chatStickers.Add(metadata);
            }
        }

        return chatStickers;
    }
}
