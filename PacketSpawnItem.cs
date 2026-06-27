using ProtoBuf;

[ProtoContract]
public class PacketSpawnItem
{
    [ProtoMember(1)]
    public string ItemCode; // Or int ItemId, but Code is often safer for lookups

    [ProtoMember(2)]
    public int Quantity;

    [ProtoMember(3)]
    public string Type; // "Item" or "Block" to differentiate
}