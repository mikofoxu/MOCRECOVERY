using System.Buffers.Binary;

namespace MocRecovery;

internal static class WarpTransforms
{
    // A warp's kind-specific index addresses the flag table. Rotation deformers
    // have their own index space even when interleaved with warps in the main list.
    internal static bool?[] Read(byte[] moc, int deformerCount)
    {
        bool bigEndian = moc[5] == 1;
        int Word(int address)
        {
            if (address < 0 || address > moc.Length - 4)
                throw new InvalidDataException("Truncated warp transform metadata.");
            return bigEndian ? BinaryPrimitives.ReadInt32BigEndian(moc.AsSpan(address))
                             : BinaryPrimitives.ReadInt32LittleEndian(moc.AsSpan(address));
        }
        int Table(int slot, int count)
        {
            int address = Word(64 + 4 * slot);
            if (address < 704 || count < 0 || address + 4L * count > moc.Length)
                throw new InvalidDataException("Warp transform table is out of bounds.");
            return address;
        }

        var result = new bool?[deformerCount];
        int warpCount = Word(Table(0, 3) + 8);
        if (warpCount == 0) return result;
        if (warpCount < 0 || warpCount > deformerCount)
            throw new InvalidDataException("Invalid number of warp deformers.");
        int kinds = Table(17, deformerCount);
        int indices = Table(18, deformerCount);
        // Cubism 3.0 models predate the quad transform flag.
        bool hasFlags = moc[4] >= 2;
        int flags = hasFlags ? Table(101, warpCount) : 0;
        var seen = new HashSet<int>();
        for (int i = 0; i < deformerCount; ++i)
        {
            if (Word(kinds + 4 * i) != 0) continue;
            int index = Word(indices + 4 * i);
            if ((uint)index >= (uint)warpCount || !seen.Add(index))
                throw new InvalidDataException("Invalid warp-specific index.");
            result[i] = hasFlags && Word(flags + 4 * index) != 0;
        }
        if (seen.Count != warpCount) throw new InvalidDataException("Warp count mismatch.");
        return result;
    }
}
