using System.Buffers.Binary;

namespace MocRecovery;

internal sealed class MocIntTables(byte[] bytes)
{
    int Word(int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("Truncated MOC3 table.");
        var span = bytes.AsSpan(offset, 4);
        return bytes[5] == 0 ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span);
    }
    internal int At(int slot,int index)
    {
        int start=Word(64+4*slot);
        if(start<704 || index<0) throw new InvalidDataException("Invalid MOC3 offset.");
        int address=checked(start+4*index);
        // Sections may be empty and share offsets. Limit access to the current table.
        int next=Word(64+4*(slot+1));
        if(next>=start && address>next-4)
            throw new InvalidDataException("Blend shape reference exceeds its section.");
        return Word(address);
    }
}
