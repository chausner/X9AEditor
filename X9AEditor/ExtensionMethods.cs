﻿using System.IO;
using System.Linq;
using System.Text;

namespace X9AEditor;

internal static class ExtensionMethods
{
    public static string ReadString(this BinaryReader reader, int length)
    {
        return new string(reader.ReadChars(length));
    }

    public static string ReadNullTerminatedString(this BinaryReader reader)
    {
        StringBuilder result = new StringBuilder();
        char c;
        while ((c = reader.ReadChar()) != '\0')
            result.Append(c);
        return result.ToString();
    }

    public static void ExpectByte(this BinaryReader reader, byte expectedValue)
    {
        if (reader.ReadByte() != expectedValue)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectBytes(this BinaryReader reader, byte[] expectedValues)
    {
        byte[] values = reader.ReadBytes(expectedValues.Length);
        if (!values.SequenceEqual(expectedValues))
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectUInt16(this BinaryReader reader, ushort expectedValue)
    {
        if (reader.ReadUInt16() != expectedValue)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectBigEndianUInt16(this BinaryReader reader, ushort expectedValue)
    {
        if (reader.ReadBigEndianUInt16() != expectedValue)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectUInt32(this BinaryReader reader, uint expectedValue)
    {
        if (reader.ReadUInt32() != expectedValue)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectBigEndianUInt32(this BinaryReader reader, uint expectedValue)
    {
        if (reader.ReadBigEndianUInt32() != expectedValue)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static void ExpectString(this BinaryReader reader, string s)
    {
        if (new string(reader.ReadChars(s.Length)) != s)
            throw new InvalidDataException("Unexpected value read.");
    }

    public static ushort ReadBigEndianUInt16(this BinaryReader reader)
    {
        ushort value = reader.ReadUInt16();
        value = (ushort)(((value & 0x00ff) << 8) | ((value & 0xff00) >> 8));
        return value;
    }

    public static uint ReadBigEndianUInt32(this BinaryReader reader)
    {
        uint value = reader.ReadUInt32();
        value = ((value & 0x000000ff) << 24) | ((value & 0x0000ff00) << 8) | ((value & 0x00ff0000) >> 8) | ((value & 0xff000000) >> 24);
        return value;
    }

    public static void WriteBigEndian(this BinaryWriter writer, ushort value)
    {
        value = (ushort)(((value & 0x00ff) << 8) | ((value & 0xff00) >> 8));
        writer.Write(value);
    }

    public static void WriteBigEndian(this BinaryWriter writer, uint value)
    {
        value = ((value & 0x000000ff) << 24) | ((value & 0x0000ff00) << 8) | ((value & 0x00ff0000) >> 8) | ((value & 0xff000000) >> 24);
        writer.Write(value);
    }

    public static void WriteNullTerminatedString(this BinaryWriter writer, string s)
    {
        writer.Write(s.ToCharArray());
        writer.Write((byte)0x00);
    }
}
