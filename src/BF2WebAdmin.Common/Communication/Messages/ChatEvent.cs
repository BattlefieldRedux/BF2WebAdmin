﻿using BF2WebAdmin.Common.Abstractions;
using BF2WebAdmin.Common.Communication.DTOs;
using MessagePack;
using ProtoBuf;

namespace BF2WebAdmin.Common.Communication.Messages
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    [MessagePackObject(keyAsPropertyName: true)]
    public class ChatEvent : IMessagePayload
    {
        public MessageDto Message { get; set; }
    }
}