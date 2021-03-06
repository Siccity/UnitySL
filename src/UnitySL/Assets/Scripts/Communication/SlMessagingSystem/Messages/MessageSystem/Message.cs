﻿using System;
using System.Collections.Generic;

public enum MessageId : UInt32
{
    StartPingCheck               = 0x00000001,
    CompletePingCheck            = 0x00000002,
    AgentUpdate                  = 0x00000004,
    LayerData                    = 0x0000000b,
    ObjectUpdate                 = 0x0000000c,
    ObjectUpdateCompressed       = 0x0000000d,
    SoundTrigger                 = 0x0000001d,

    CoarseLocationUpdate         = 0x0000ff06,
    AttachedSound                = 0x0000ff0d,
    PreloadSound                 = 0x0000ff0f,
    ViewerEffect                 = 0x0000ff11,

    Wrapper                      = 0xffff0001,
    UseCircuitCode               = 0xffff0003,
    ChatFromViewer               = 0xffff0050,
    AgentThrottle                = 0xffff0051,
    AgentHeightWidth             = 0xffff0053,
    HealthMessage                = 0xffff008a,
    ChatFromSimulator            = 0xffff008b,
    RegionHandshake              = 0xffff0094,
    RegionHandshakeReply         = 0xffff0095,
    SimulatorViewerTimeMessage   = 0xffff0096,
    ScriptControlChange          = 0xffff00bd,
    CompleteAgentMovement        = 0xffff00f9,
    AgentMovementCompleteMessage = 0xffff00fa,
    LogoutRequest                = 0xffff00fc,
    LogoutReply                  = 0xffff00fd,
    OnlineNotification           = 0xffff0142,
    OfflineNotification          = 0xffff0143,
    AgentDataUpdateRequest       = 0xffff0182,
    AgentDataUpdate              = 0xffff0183,
    PacketAck                    = 0xfffffffb,
    OpenCircuit                  = 0xfffffffc
}
[Flags] public enum PacketFlags : byte
{
    ZeroCode = 0x80,
    Reliable = 0x40,
    Resent = 0x20,
    Ack = 0x10
}

public enum MessageFrequency : UInt32
{
    High, 
    Medium,
    Low,
    Fixed
}

public enum MessageTrustLevel
{
    Trusted,
    NotTrusted
}

public enum MessageEncoding
{
    Unencoded,
    Zerocoded
}

public class Message
{
    /// <summary>
    /// MTU - The largest total size of a packet.
    /// </summary>
    public static readonly int MaximumTranferUnit = 1200;

    public PacketFlags Flags { get; set; }
    public UInt32 SequenceNumber { get; set; }
    public byte[] ExtraHeader { get; set; }


    public string Name { get; set; }

    public MessageFrequency Frequency
    {
        get
        {
            UInt32 id = (UInt32)MessageId;
            if (id >= 0xfffffffa)
            {
                return MessageFrequency.Fixed;
            }

            if (id >= 0xffff0000)
            {
                return MessageFrequency.Low;
            }

            if (id >= 0xff00)
            {
                return MessageFrequency.Medium;
            }

            return MessageFrequency.High;
        }
    }

    public MessageId MessageId { get; set; }
    public MessageTrustLevel TrustLevel { get; set; }

    public List<UInt32> Acks { get; set; } = new List<UInt32>();

    public void AddAck(UInt32 sequenceNumber)
    {
        if (Acks.Count >= 255)
        {
            throw new Exception("Messasge: Too many acks in a single message. Max is 255.");
        }
        Acks.Add(sequenceNumber);
    }

    #region Serailise
    public virtual int GetSerializedLength()
    {
        int size = 1                           // Flags
                 + 4                           // SequenceNumber
                 + 1                           // Extra header length
                 + (ExtraHeader?.Length ?? 0); // Extra header
        switch (Frequency)
        {
            case MessageFrequency.High:
                size += 1; // Message Id
                break;
            case MessageFrequency.Medium:
                size += 2; // Message Id
                break;
            case MessageFrequency.Low:
            case MessageFrequency.Fixed:
                size += 4; // Message Id
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Frequency), "Message.GetSerializedLength(Message): Unknown message frequency.");
        }

        if (Acks.Count != 0)
        {
            size += Acks.Count * 4 + 1;
        }
        return size;
    }

    public virtual int Serialize(byte[] buffer, int offset, int length)
    {
        if (length - offset < GetSerializedLength())
        {
            throw new ArgumentOutOfRangeException(nameof(offset),
                "Message.Serialize: Not enough room in the target buffer.");
        }

        int o = offset;

        if (Acks.Count != 0)
        {
            Flags |= PacketFlags.Ack;
        }

        buffer[o++] = (byte)Flags;
        o = BinarySerializer.Serialize_Be (SequenceNumber, buffer, o, length);
        buffer[o++] = (byte)(ExtraHeader?.Length ?? 0);

        UInt32 id = (UInt32)MessageId;
        if (Frequency == MessageFrequency.Fixed || Frequency == MessageFrequency.Low)
        {
            buffer[o++] = (byte)(id >> 24);
            buffer[o++] = (byte)(id >> 16);
        }
        if (Frequency == MessageFrequency.Medium || Frequency == MessageFrequency.Fixed || Frequency == MessageFrequency.Low)
        {
            buffer[o++] = (byte)(id >> 8);
        }
        buffer[o++] = (byte)(id >> 0);

        // Acks come at the end of the buffer. WARNING: If the buffer is too small, acks will be overwritten!
        BinarySerializer.SerializeAcks(Acks, buffer);

        return o - offset;
    }
    #endregion Serailise

    #region DeSerialise
    /// <summary>
    /// Keeps track of messageIds we received but can't decode yet.
    /// </summary>
    public static HashSet<UInt32> UnknownMessageIds = new HashSet<uint>();

    protected static Dictionary<MessageId, Func<Message>> MessageCreator = new Dictionary<MessageId, Func<Message>>
    {
        { MessageId.StartPingCheck,               () => new StartPingCheckMessage()          },
        { MessageId.CompletePingCheck,            () => new CompletePingCheckMessage()       },
        { MessageId.LayerData,                    () => new LayerDataMessage()               },
        { MessageId.ObjectUpdate,                 () => new ObjectUpdateMessage()            },
        { MessageId.ObjectUpdateCompressed,       () => new ObjectUpdateCompressedMessage()  },
        { MessageId.SoundTrigger,                 () => new SoundTriggerMessage()            },
        { MessageId.CoarseLocationUpdate,         () => new CoarseLocationUpdateMessage()    },
        { MessageId.AttachedSound,                () => new AttachedSoundMessage()           },
        { MessageId.PreloadSound,                 () => new PreloadSoundMessage()            },
        { MessageId.ViewerEffect,                 () => new ViewerEffectMessage()            },
        { MessageId.HealthMessage,                () => new HealthMessage()                  },
        { MessageId.ChatFromSimulator,            () => new ChatFromSimulatorMessage()       },
        { MessageId.RegionHandshake,              () => new RegionHandshakeMessage()         },
        { MessageId.SimulatorViewerTimeMessage,   () => new SimulatorViewerTimeMessage()     },
        { MessageId.ScriptControlChange,          () => new ScriptControlChangeMessage()     },
        { MessageId.AgentMovementCompleteMessage, () => new AgentMovementCompleteMessage()   },
        { MessageId.LogoutReply,                  () => new LogoutReplyMessage()             },
        { MessageId.OnlineNotification,           () => new OnlineNotificationMessage()      },
        { MessageId.OfflineNotification,          () => new OfflineNotificationMessage()     },
        { MessageId.AgentDataUpdate,              () => new AgentDataUpdateMessage()         },
        { MessageId.PacketAck,                    () => new PacketAckMessage()               },
    };

/* Blank message snippet to copy from for new messages:

    public X()
    {
        Id = MessageId.X;
        Flags = 0;
        Frequency = MessageFrequency.X;
    }

    #region DeSerialise
    protected override void DeSerialise(byte[] buf, ref int o, int length)
    {
    }
    #endregion DeSerialise

    public override string ToString()
    {
        return $"{base.ToString()}: Y={Y}, Z={Z}";
    }
*/

    /// <summary>
    /// Creates a message based on the data in the given buffer.
    /// </summary>
    /// <param name="buf"></param>
    /// <param name="o"></param>
    /// <returns></returns>
    public static Message DeSerializeMessage (byte[] buf, ref int o)
    {
        if (buf.Length - o < 6)
        {
            throw new Exception("Message.CreateMessage: Not enough room in buffer.");
        }

        #region Header
        PacketFlags packetFlags = (PacketFlags)buf[o++];
        UInt32 sequenceNumber = (((UInt32)buf[o++]) << 24)
                              + (((UInt32)buf[o++]) << 16)
                              + (((UInt32)buf[o++]) << 8)
                              + (((UInt32)buf[o++]) << 0);
        byte extraHeaderLength = buf[o++];
        byte[] extraHeader = new byte[extraHeaderLength];
        for (int i = 0; i < extraHeaderLength; i++)
        {
            extraHeader[i] = buf[o++];
        }
        #endregion Header

        #region AppendedAcks
        List<UInt32> acks = new List<UInt32>();
        int ackLength = 0;
        if ((packetFlags & PacketFlags.Ack) != 0)
        {
            byte nAcks = buf[buf.Length - 1];
            ackLength = nAcks * 4 + 1;
            int ackOffset = buf.Length - ackLength;
            for (int i = 0; i < nAcks; i++)
            {
                UInt32 ack = BinarySerializer.DeSerializeUInt32_Be(buf, ref ackOffset, buf.Length);
                acks.Add(ack);
            }
        }
        #endregion AppendedAcks

        #region MessageId
        MessageFrequency frequency = MessageFrequency.High;
        UInt32 id = buf[o++];
        if (id == 0xff)
        {
            id = (id << 8) + buf[o++];
            frequency = MessageFrequency.Medium;

            if (id == 0xffff)
            {
                id = id << 16;
                id += ((UInt32)buf[o++]) << 8;
                id += ((UInt32)buf[o++]) << 0;
                frequency = id < 0xfffffffa ? MessageFrequency.Low : MessageFrequency.Fixed;
            }
        }

        if (id == (UInt32)MessageId.Wrapper)
        {
            id = 0xffff0000 + buf[o++];
        }

        if (Enum.IsDefined(typeof(MessageId), id) == false || MessageCreator.ContainsKey ((MessageId)id) == false)
        {
            string idString = "";
            switch (frequency)
            {
                case MessageFrequency.High:
                case MessageFrequency.Medium:
                    idString = $"{frequency} {id & 0xff} (0x{id:x8})";
                    break;

                case MessageFrequency.Low:
                    idString = $"{frequency} {id & 0xffff} (0x{id:x8})";
                    break;

                case MessageFrequency.Fixed:
                    idString = $"{frequency} 0x{id:x8}";
                    break;
            }

            // Only log unknown messages the first time to reduce spam and lag:
            if (UnknownMessageIds.Contains(id) == false)
            {
                Logger.LogError($"BinarySerializer.DeSerializeMessage: Unknown message id {idString}");
                UnknownMessageIds.Add(id);
            }

            return new Message
            {
                Flags          = packetFlags,
                SequenceNumber = sequenceNumber,
                Acks           = acks,
                ExtraHeader    = extraHeader
            };
        }
        MessageId messageId = (MessageId)id;
        #endregion MessageId

        #region DataBuffer
        byte[] dataBuffer = buf;
        int dataOffset = o;
        int dataLen = buf.Length - ackLength;
        if ((packetFlags & PacketFlags.ZeroCode) != 0)
        {
            dataBuffer = BinarySerializer.ExpandZerocode (buf, o, dataLen - o);
            dataOffset = 0;
            dataLen = dataBuffer.Length;
        }
        #endregion DataBuffer

        Message message = MessageCreator[messageId]();
        message.Flags          = packetFlags;
        message.SequenceNumber = sequenceNumber;
        message.ExtraHeader    = extraHeader;
        message.Acks           = acks;

        message.DeSerialise (dataBuffer, ref dataOffset, dataLen);
        //Logger.LogDebug ($"Message.DeSerialiseMessage: {message}");
        return message;
    }

    protected virtual void DeSerialise(byte[] buf, ref int offset, int length)
    {
        throw new NotImplementedException("Top level Message.DeSerialise called!");
    }
    #endregion DeSerialise

    public override string ToString()
    {
        string idNumber;
        switch (Frequency)
        {
            case MessageFrequency.High:
                idNumber = ((byte)MessageId).ToString();
                break;
            case MessageFrequency.Medium:
                idNumber = ((byte)MessageId).ToString();
                break;
            case MessageFrequency.Low:
                idNumber = ((UInt16)MessageId).ToString();
                break;
            case MessageFrequency.Fixed:
                idNumber = ((UInt32)MessageId).ToString("x8");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return $"{MessageId} ({Frequency} {idNumber}) Seq={SequenceNumber}, Flags={Flags}";
    }
}
