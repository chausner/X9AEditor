using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace X9AEditor;

class X9aFile
{
    const uint EntryListEntryPadSize = 0x400;
    const uint DataListEntryPadSize = 0x400;
    const uint EntrySystemEntryPadSize = 0x200;
    const uint DataSystemEntryPadSize = 0x200;

    public Voice[] Voices { get; }
    public SystemData System { get; }

    private X9aFile(BinaryReader binaryReader)
    {
        Header header = ParseHeader(binaryReader);
        Dictionary<string, CatalogueEntry> catalogue = ParseCatalogue(binaryReader, header.CatalogueSize);

        if (!catalogue.Keys.SequenceEqual(["ELST", "ESYS", "DLST", "DSYS"]))
            throw new InvalidDataException("Unexpected catalogue: " + string.Join(", ", catalogue.Keys));

        CatalogueEntry elst = catalogue["ELST"];
        CatalogueEntry esys = catalogue["ESYS"];
        CatalogueEntry dlst = catalogue["DLST"];
        CatalogueEntry dsys = catalogue["DSYS"];

        // voices
        EntryListEntry[] entryListEntries = ParseEntryList(binaryReader, elst);
        Voices = new Voice[entryListEntries.Length];

        for (int i = 0; i < entryListEntries.Length; i++)
        {
            binaryReader.BaseStream.Seek(dlst.Offset + entryListEntries[i].DataOffset + 8, SeekOrigin.Begin);
            byte[] data = binaryReader.ReadBytes((int)entryListEntries[i].DataSize);

            using (MemoryStream memoryStream = new MemoryStream(data, false))
            using (BinaryReader binaryReader2 = new BinaryReader(memoryStream, new YamahaEncoding()))
                Voices[i] = ParseDataList(binaryReader2);

            if (entryListEntries[i].LiveSetPage != i / 8)
                throw new InvalidDataException($"Unexpected live set page");
            if (entryListEntries[i].LiveSetIndex != i % 8)
                throw new InvalidDataException($"Unexpected live set index");
            if (entryListEntries[i].EntryName != Voices[i].Name)
                throw new InvalidDataException($"Unexpected entry list entry name");
        }

        // system data
        EntrySystemEntry[] entrySystemEntries = ParseEntrySystem(binaryReader, esys);

        if (entrySystemEntries.Length != 1)
            throw new InvalidDataException($"Expected exactly one system entry but found {entrySystemEntries.Length}");

        {
            binaryReader.BaseStream.Seek(dsys.Offset + entrySystemEntries[0].DataOffset + 8, SeekOrigin.Begin);
            byte[] data = binaryReader.ReadBytes((int)entrySystemEntries[0].DataSize);

            using (MemoryStream memoryStream = new MemoryStream(data, false))
            using (BinaryReader binaryReader2 = new BinaryReader(memoryStream, new YamahaEncoding()))
                System = ParseDataSystem(binaryReader2);
        }
    }

    public static X9aFile Parse(string path)
    {
        using (FileStream fileStream = File.OpenRead(path))
            return Parse(fileStream);
    }

    public static X9aFile Parse(Stream stream)
    {
        using (BinaryReader binaryReader = new BinaryReader(stream, new YamahaEncoding(), true))
            return new X9aFile(binaryReader);
    }

    public void Save(string path)
    {
        using (FileStream fileStream = File.Create(path))
            Save(fileStream);
    }

    public void Save(Stream stream)
    {
        using (BinaryWriter binaryWriter = new BinaryWriter(stream, new YamahaEncoding(), true))
        {
            Dictionary<string, CatalogueEntry> catalogue = new Dictionary<string, CatalogueEntry>();
            catalogue["ELST"] = new CatalogueEntry() { ID = "ELST" };
            catalogue["ESYS"] = new CatalogueEntry() { ID = "ESYS" };
            catalogue["DLST"] = new CatalogueEntry() { ID = "DLST" };
            catalogue["DSYS"] = new CatalogueEntry() { ID = "DSYS" };

            Header header = new Header() { CatalogueSize = (uint)(catalogue.Count * 8) };
            WriteHeader(binaryWriter, header);

            long catalogueStart = binaryWriter.BaseStream.Position;
            WriteCatalogue(binaryWriter, catalogue); // catalogue does not contain proper offsets yet

            EntryListEntry[] entryListEntries = new EntryListEntry[Voices.Length];
            for (int i = 0; i < entryListEntries.Length; i++)
            {
                entryListEntries[i] = new EntryListEntry
                {
                    DataOffset = sizeof(uint) + (2 * sizeof(uint)) + (uint)(i * (EntryListEntryPadSize + 2 * sizeof(uint))),
                    DataSize = EntryListEntryPadSize,
                    LiveSetPage = (byte)(i / 8),
                    LiveSetIndex = (byte)(i % 8),
                    EntryName = Voices[i].Name
                };
            }
            catalogue["ELST"].Offset = (uint)binaryWriter.BaseStream.Position;
            WriteEntryList(binaryWriter, entryListEntries);

            EntrySystemEntry[] entrySystemEntries = new EntrySystemEntry[1];
            for (int i = 0; i < entrySystemEntries.Length; i++)
            {
                entrySystemEntries[i] = new EntrySystemEntry
                {
                    DataOffset = sizeof(uint) + (2 * sizeof(uint)) + (uint)(i * (EntrySystemEntryPadSize + 2 * sizeof(uint))),
                    DataSize = EntrySystemEntryPadSize
                };
            }
            catalogue["ESYS"].Offset = (uint)binaryWriter.BaseStream.Position;
            WriteEntrySystem(binaryWriter, entrySystemEntries);

            catalogue["DLST"].Offset = (uint)binaryWriter.BaseStream.Position;
            binaryWriter.Write("DLST".AsSpan());
            using (WriteBlockLength(binaryWriter))
            {
                binaryWriter.WriteBigEndian((uint)Voices.Length);
                foreach (Voice voice in Voices)
                    WriteDataList(binaryWriter, voice);
            }

            catalogue["DSYS"].Offset = (uint)binaryWriter.BaseStream.Position;
            binaryWriter.Write("DSYS".AsSpan());
            using (WriteBlockLength(binaryWriter))
            {
                binaryWriter.WriteBigEndian(1U);
                WriteDataSystem(binaryWriter, System);
            }

            long endPosition = binaryWriter.BaseStream.Position;
            binaryWriter.BaseStream.Seek(catalogueStart, SeekOrigin.Begin);
            WriteCatalogue(binaryWriter, catalogue); // write catalogue a second time, this time with proper offsets
            binaryWriter.BaseStream.Seek(endPosition, SeekOrigin.Begin);
        }
    }

    private Header ParseHeader(BinaryReader binaryReader)
    {
        binaryReader.ExpectString("YAMAHA-YSFC\0");
        binaryReader.ExpectUInt32(0); // unknown
        binaryReader.ExpectString("6.0.0\0");
        binaryReader.ExpectBytes(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        uint catalogueSize = binaryReader.ReadBigEndianUInt32();
        binaryReader.ExpectBytes(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        return new Header { CatalogueSize = catalogueSize };
    }

    private void WriteHeader(BinaryWriter binaryWriter, Header header)
    {
        binaryWriter.Write("YAMAHA-YSFC\0".AsSpan());
        binaryWriter.Write(0); // unknown
        binaryWriter.Write("6.0.0\0".AsSpan());
        binaryWriter.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        binaryWriter.WriteBigEndian(header.CatalogueSize);
        binaryWriter.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
    }

    private Dictionary<string, CatalogueEntry> ParseCatalogue(BinaryReader binaryReader, uint catalogueSize)
    {
        uint numEntries = catalogueSize / 8;
        Dictionary<string, CatalogueEntry> catalogue = new Dictionary<string, CatalogueEntry>((int)numEntries);

        for (uint i = 0; i < numEntries; i++)
        {
            var entry = new CatalogueEntry
            {
                ID = binaryReader.ReadString(4),
                Offset = binaryReader.ReadBigEndianUInt32()
            };
            catalogue[entry.ID] = entry;
        }

        return catalogue;
    }

    private void WriteCatalogue(BinaryWriter binaryWriter, Dictionary<string, CatalogueEntry> catalogue)
    {
        foreach (CatalogueEntry entry in catalogue.Values)
        {
            binaryWriter.Write(entry.ID.AsSpan());
            binaryWriter.WriteBigEndian(entry.Offset);
        }
    }

    private EntryListEntry[] ParseEntryList(BinaryReader binaryReader, CatalogueEntry catalogueEntry)
    {
        binaryReader.BaseStream.Seek(catalogueEntry.Offset, SeekOrigin.Begin);
        binaryReader.ExpectString(catalogueEntry.ID);
        uint blockLength = binaryReader.ReadBigEndianUInt32();
        long blockStart = binaryReader.BaseStream.Position;

        uint numEntries = binaryReader.ReadBigEndianUInt32();
        EntryListEntry[] entries = new EntryListEntry[numEntries];

        for (int i = 0; i < numEntries; i++)
        {
            binaryReader.ExpectString("Entr");
            uint entrySize = binaryReader.ReadBigEndianUInt32();
            long entryStart = binaryReader.BaseStream.Position;
            uint dataSize = binaryReader.ReadBigEndianUInt32();
            uint dataOffset = binaryReader.ReadBigEndianUInt32();
            binaryReader.ExpectBigEndianUInt16(0x3F); // unknown
            byte liveSetPage = binaryReader.ReadByte();
            byte liveSetIndex = binaryReader.ReadByte();
            string entryName = binaryReader.ReadNullTerminatedString();

            entries[i] = new EntryListEntry
            {
                DataOffset = dataOffset,
                DataSize = dataSize,
                LiveSetPage = liveSetPage,
                LiveSetIndex = liveSetIndex,
                EntryName = entryName
            };

            if (entrySize != binaryReader.BaseStream.Position - entryStart)
                throw new InvalidDataException($"Expected entry size {binaryReader.BaseStream.Position - entryStart} but got {entrySize}");
        }

        if (blockLength != binaryReader.BaseStream.Position - blockStart)
            throw new InvalidDataException($"Expected block length {binaryReader.BaseStream.Position - blockStart} but got {blockLength}");

        return entries;
    }

    private void WriteEntryList(BinaryWriter binaryWriter, EntryListEntry[] entries)
    {
        binaryWriter.Write("ELST".AsSpan());
        using (WriteBlockLength(binaryWriter))
        {
            binaryWriter.WriteBigEndian((uint)entries.Length);

            foreach (EntryListEntry entry in entries)
            {
                binaryWriter.Write("Entr".AsSpan());
                using (WriteBlockLength(binaryWriter))
                {
                    binaryWriter.WriteBigEndian(entry.DataSize);
                    binaryWriter.WriteBigEndian(entry.DataOffset);
                    binaryWriter.WriteBigEndian((ushort)0x3F);
                    binaryWriter.Write(entry.LiveSetPage);
                    binaryWriter.Write(entry.LiveSetIndex);
                    binaryWriter.WriteNullTerminatedString(entry.EntryName);
                }
            }
        }
    }

    private EntrySystemEntry[] ParseEntrySystem(BinaryReader binaryReader, CatalogueEntry catalogueEntry)
    {
        binaryReader.BaseStream.Seek(catalogueEntry.Offset, SeekOrigin.Begin);
        binaryReader.ExpectString(catalogueEntry.ID);
        uint blockLength = binaryReader.ReadBigEndianUInt32();
        long blockStart = binaryReader.BaseStream.Position;

        uint numEntries = binaryReader.ReadBigEndianUInt32();
        EntrySystemEntry[] entries = new EntrySystemEntry[numEntries];

        for (int i = 0; i < numEntries; i++)
        {
            binaryReader.ExpectString("Entr");
            uint entrySize = binaryReader.ReadBigEndianUInt32();
            long entryStart = binaryReader.BaseStream.Position;
            uint dataSize = binaryReader.ReadBigEndianUInt32();
            uint dataOffset = binaryReader.ReadBigEndianUInt32();
            binaryReader.ExpectBigEndianUInt16(0x00); // unknown
            binaryReader.ExpectBigEndianUInt16(0x00); // unknown
            string entryName = binaryReader.ReadNullTerminatedString();
            if (entryName != "System")
                throw new InvalidDataException();
            entries[i] = new EntrySystemEntry
            {
                DataOffset = dataOffset,
                DataSize = dataSize
            };

            if (entrySize != binaryReader.BaseStream.Position - entryStart)
                throw new InvalidDataException($"Expected entry size {binaryReader.BaseStream.Position - entryStart} but got {entrySize}");
        }

        if (blockLength != binaryReader.BaseStream.Position - blockStart)
            throw new InvalidDataException($"Expected block length {binaryReader.BaseStream.Position - blockStart} but got {blockLength}");

        return entries;
    }

    private void WriteEntrySystem(BinaryWriter binaryWriter, EntrySystemEntry[] entries)
    {
        binaryWriter.Write("ESYS".AsSpan());
        using (WriteBlockLength(binaryWriter))
        {
            binaryWriter.WriteBigEndian((uint)entries.Length);

            foreach (EntrySystemEntry entry in entries)
            {
                binaryWriter.Write("Entr".AsSpan());
                using (WriteBlockLength(binaryWriter))
                {
                    binaryWriter.WriteBigEndian(entry.DataSize);
                    binaryWriter.WriteBigEndian(entry.DataOffset);
                    binaryWriter.WriteBigEndian((ushort)0x00);
                    binaryWriter.WriteBigEndian((ushort)0x00);
                    binaryWriter.WriteNullTerminatedString("System");
                }
            }
        }
    }

    private SystemData ParseDataSystem(BinaryReader binaryReader)
    {
        SystemData systemData = SystemData.Read(binaryReader);
        
        // padding
        byte[] padding = binaryReader.ReadBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position));
        if (padding.Any(b => b != 0xFF))
            throw new InvalidDataException();

        return systemData;
    }

    private void WriteDataSystem(BinaryWriter binaryWriter, SystemData systemData)
    {
        binaryWriter.Write("Data".AsSpan());
        binaryWriter.WriteBigEndian(DataSystemEntryPadSize);

        long startPosition = binaryWriter.BaseStream.Position;

        systemData.WriteTo(binaryWriter);

        int numPadBytes = (int)(DataSystemEntryPadSize - (binaryWriter.BaseStream.Position - startPosition));
        binaryWriter.Write(Enumerable.Repeat((byte)0xFF, numPadBytes).ToArray());
    }

    private Voice ParseDataList(BinaryReader binaryReader)
    {
        Voice voice = Voice.Read(binaryReader);

        // padding
        byte[] padding = binaryReader.ReadBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position));
        if (padding.Any(b => b != 0xFF))
            throw new InvalidDataException();

        return voice;
    }

    private void WriteDataList(BinaryWriter binaryWriter, Voice voice)
    {
        binaryWriter.Write("Data".AsSpan());
        binaryWriter.WriteBigEndian(DataListEntryPadSize);

        long startPosition = binaryWriter.BaseStream.Position;

        voice.WriteTo(binaryWriter);

        // padding
        int numPadBytes = (int)(DataListEntryPadSize - (binaryWriter.BaseStream.Position - startPosition));
        binaryWriter.Write(Enumerable.Repeat((byte)0xFF, numPadBytes).ToArray());
    }

    private IDisposable WriteBlockLength(BinaryWriter binaryWriter)
    {
        return new WriteBlockLengthHelper(binaryWriter);
    }

    private class WriteBlockLengthHelper : IDisposable
    {
        readonly BinaryWriter binaryWriter;
        readonly long startPosition;
        bool disposed = false;

        public WriteBlockLengthHelper(BinaryWriter binaryWriter)
        {
            this.binaryWriter = binaryWriter;
            binaryWriter.BaseStream.Seek(sizeof(uint), SeekOrigin.Current);
            startPosition = binaryWriter.BaseStream.Position;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    uint blockLength = (uint)(binaryWriter.BaseStream.Position - startPosition);
                    binaryWriter.BaseStream.Seek(-blockLength - sizeof(uint), SeekOrigin.Current);
                    binaryWriter.WriteBigEndian(blockLength);
                    binaryWriter.BaseStream.Seek(blockLength, SeekOrigin.Current);
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    private record Header
    {
        public uint CatalogueSize;
    }

    private record CatalogueEntry
    {
        public string ID;
        public uint Offset;
    }

    private record EntrySystemEntry
    {
        public uint DataOffset;
        public uint DataSize;
    }

    private record EntryListEntry
    {
        public uint DataOffset;
        public uint DataSize;
        public byte LiveSetPage;
        public byte LiveSetIndex;
        public string EntryName;
    }

    [Serializable]
    public class Voice : ICloneable
    {
        public string Name;
        public byte Unknown;
        public byte MasterSwitch;
        public byte AdvancedZoneSwitch;
        public byte Transpose;
        public byte SplitPoint;
        public byte DelayReverbSectionSelection;
        public byte ModulationLeverAssign;
        public byte ModulationLeverLimitLow;
        public byte ModulationLeverLimitHigh;
        public byte FC1Assign;
        public byte FC1LimitLow;
        public byte FC1LimitHigh;
        public byte FC2Assign;
        public byte FC2LimitLow;
        public byte FC2LimitHigh;
        public LiveSetEQ? LiveSetEQ;

        public Delay Delay;
        public Reverb Reverb;
        public MasterKeyboardZone[] MasterKeyboardZones;
        public Section[] Sections;
        public LiveSetEQ2? LiveSetEQ2;

        public static Voice Read(BinaryReader binaryReader)
        {
            Voice voice = new Voice();

            uint nameLength = binaryReader.ReadBigEndianUInt32();
            if (nameLength != 16)
                throw new InvalidDataException();
            voice.Name = binaryReader.ReadString((int)nameLength).TrimEnd('\0');

            // struct 1
            uint structLength = binaryReader.ReadBigEndianUInt32();
            if (structLength != 0x11 && structLength != 0x17)
                throw new InvalidDataException();
            voice.Unknown = binaryReader.ReadByte();
            voice.MasterSwitch = binaryReader.ReadByte();
            voice.AdvancedZoneSwitch = binaryReader.ReadByte();
            voice.Transpose = binaryReader.ReadByte();
            voice.SplitPoint = binaryReader.ReadByte();
            byte FC1assign = binaryReader.ReadByte();
            byte FC2assign = binaryReader.ReadByte();
            voice.DelayReverbSectionSelection = binaryReader.ReadByte();
            voice.ModulationLeverAssign = binaryReader.ReadByte();
            voice.ModulationLeverLimitLow = binaryReader.ReadByte();
            voice.ModulationLeverLimitHigh = binaryReader.ReadByte();
            voice.FC1Assign = binaryReader.ReadByte();
            voice.FC1LimitLow = binaryReader.ReadByte();
            voice.FC1LimitHigh = binaryReader.ReadByte();
            voice.FC2Assign = binaryReader.ReadByte();
            voice.FC2LimitLow = binaryReader.ReadByte();
            voice.FC2LimitHigh = binaryReader.ReadByte();
            if (FC1assign != voice.FC1Assign || FC2assign != voice.FC2Assign)
                throw new InvalidDataException();
            if (structLength == 0x17)
                voice.LiveSetEQ = LiveSetEQ.Read(binaryReader);

            // delay
            voice.Delay = Delay.Read(binaryReader);

            // reverb
            voice.Reverb = Reverb.Read(binaryReader);

            // master keyboard zones
            uint numMasterKeyboardZones = binaryReader.ReadBigEndianUInt32();
            if (numMasterKeyboardZones != 4)
                throw new InvalidDataException();
            voice.MasterKeyboardZones = new MasterKeyboardZone[numMasterKeyboardZones];
            for (int i = 0; i < numMasterKeyboardZones; i++)
                voice.MasterKeyboardZones[i] = MasterKeyboardZone.Read(binaryReader);

            // sections
            uint numSections = binaryReader.ReadBigEndianUInt32();
            if (numSections != 3)
                throw new InvalidDataException();
            voice.Sections = new Section[numSections];
            for (int i = 0; i < numSections; i++)
                voice.Sections[i] = Section.Read(binaryReader);

            // live set EQ 2
            uint liveSetEQ2Length = binaryReader.ReadBigEndianUInt32();
            if (liveSetEQ2Length != 0xFFFFFFFF)
            {
                binaryReader.BaseStream.Seek(-4, SeekOrigin.Current);
                voice.LiveSetEQ2 = LiveSetEQ2.Read(binaryReader);
            }

            return voice;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            if (Name.Length > 15)
                throw new InvalidDataException("Maximum length of voice name is 15 characters");
            binaryWriter.WriteBigEndian(0x10U);
            binaryWriter.Write(Name.PadRight(16, '\0').AsSpan());

            uint structLength = LiveSetEQ != null ? 0x17U : 0x11U;
            binaryWriter.WriteBigEndian(structLength);
            binaryWriter.Write(Unknown);
            binaryWriter.Write(MasterSwitch);
            binaryWriter.Write(AdvancedZoneSwitch);
            binaryWriter.Write(Transpose);
            binaryWriter.Write(SplitPoint);
            binaryWriter.Write(FC1Assign);
            binaryWriter.Write(FC2Assign);
            binaryWriter.Write(DelayReverbSectionSelection);
            binaryWriter.Write(ModulationLeverAssign);
            binaryWriter.Write(ModulationLeverLimitLow);
            binaryWriter.Write(ModulationLeverLimitHigh);
            binaryWriter.Write(FC1Assign);
            binaryWriter.Write(FC1LimitLow);
            binaryWriter.Write(FC1LimitHigh);
            binaryWriter.Write(FC2Assign);
            binaryWriter.Write(FC2LimitLow);
            binaryWriter.Write(FC2LimitHigh);
            LiveSetEQ?.WriteTo(binaryWriter);

            // delay
            Delay.WriteTo(binaryWriter);

            // reverb
            Reverb.WriteTo(binaryWriter);

            // master keyboard zones
            binaryWriter.WriteBigEndian((uint)MasterKeyboardZones.Length);
            foreach (MasterKeyboardZone zone in MasterKeyboardZones)
                zone.WriteTo(binaryWriter);

            // sections
            binaryWriter.WriteBigEndian((uint)Sections.Length);
            foreach (Section section in Sections)
                section.WriteTo(binaryWriter);

            // live set EQ 2
            LiveSetEQ2?.WriteTo(binaryWriter);
        }

        public object Clone()
        {
            return (Voice)MemberwiseClone();
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            Voice other = (Voice)obj;

            if (other.Name != Name) // fast path
                return false;

            using (MemoryStream memoryStream1 = new MemoryStream())
            using (MemoryStream memoryStream2 = new MemoryStream())
            using (BinaryWriter binaryWriter1 = new BinaryWriter(memoryStream1))
            using (BinaryWriter binaryWriter2 = new BinaryWriter(memoryStream2))
            {
                var tmp1 = (Voice)Clone();              
                var tmp2 = (Voice)other.Clone();

                Normalize(tmp1);
                Normalize(tmp2);

                tmp1.WriteTo(binaryWriter1);
                tmp2.WriteTo(binaryWriter2);

                byte[] b1 = memoryStream1.ToArray();
                byte[] b2 = memoryStream2.ToArray();

                return b1.SequenceEqual(b2);
            }

            void Normalize(Voice voice)
            {
                if (voice.LiveSetEQ == null)
                    voice.LiveSetEQ = LiveSetEQ.Default;
                if (voice.LiveSetEQ2 == null)
                    voice.LiveSetEQ2 = LiveSetEQ2.Default;

                foreach (Section section in voice.Sections)
                {
                    if (section.Extension == null)
                        section.Extension = SectionExtension.Default;
                    if (section.Extension2 == null)
                        section.Extension2 = SectionExtension2.Default;
                }
            }
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }

    [Serializable]
    public record LiveSetEQ
    {
        public byte LiveSetEQModeSwitch;
        public byte LiveSetEQOnOff;
        public byte LowGain;
        public byte MidGain;
        public byte MidGainFrequency;
        public byte HighGain;

        public static LiveSetEQ Read(BinaryReader binaryReader)
        {
            LiveSetEQ liveSetEQ = new LiveSetEQ();

            liveSetEQ.LiveSetEQModeSwitch = binaryReader.ReadByte();
            liveSetEQ.LiveSetEQOnOff = binaryReader.ReadByte();
            liveSetEQ.LowGain = binaryReader.ReadByte();
            liveSetEQ.MidGain = binaryReader.ReadByte();
            liveSetEQ.MidGainFrequency = binaryReader.ReadByte();
            liveSetEQ.HighGain = binaryReader.ReadByte();

            return liveSetEQ;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(LiveSetEQModeSwitch);
            binaryWriter.Write(LiveSetEQOnOff);
            binaryWriter.Write(LowGain);
            binaryWriter.Write(MidGain);
            binaryWriter.Write(MidGainFrequency);
            binaryWriter.Write(HighGain);
        }

        public static LiveSetEQ Default => new()
        {
            LiveSetEQModeSwitch = 0,
            LiveSetEQOnOff = 0,
            LowGain = 64,
            MidGain = 64,
            MidGainFrequency = 28,
            HighGain = 64
        };
    }

    [Serializable]
    public record Delay
    {
        public byte DelayOnOff;
        public byte DelayType;
        public byte DelayTime;
        public byte DelayFeedback;
        public byte PianoDelayDepth;
        public byte EPianoDelayDepth;
        public byte SubDelayDepth;

        public static Delay Read(BinaryReader binaryReader)
        {
            Delay delay = new Delay();

            binaryReader.ExpectBigEndianUInt32(0x7);
            delay.DelayOnOff = binaryReader.ReadByte();
            delay.DelayType = binaryReader.ReadByte();
            delay.DelayTime = binaryReader.ReadByte();
            delay.DelayFeedback = binaryReader.ReadByte();
            delay.PianoDelayDepth = binaryReader.ReadByte();
            delay.EPianoDelayDepth = binaryReader.ReadByte();
            delay.SubDelayDepth = binaryReader.ReadByte();

            return delay;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.WriteBigEndian(0x7U);
            binaryWriter.Write(DelayOnOff);
            binaryWriter.Write(DelayType);
            binaryWriter.Write(DelayTime);
            binaryWriter.Write(DelayFeedback);
            binaryWriter.Write(PianoDelayDepth);
            binaryWriter.Write(EPianoDelayDepth);
            binaryWriter.Write(SubDelayDepth);
        }
    }

    [Serializable]
    public record Reverb
    {
        public byte ReverbOnOff;
        public byte ReverbTime;
        public byte PianoReverbDepth;
        public byte EPianoReverbDepth;
        public byte SubReverbDepth;

        public static Reverb Read(BinaryReader binaryReader)
        {
            Reverb reverb = new Reverb();

            binaryReader.ExpectBigEndianUInt32(0x5);
            reverb.ReverbOnOff = binaryReader.ReadByte();
            reverb.ReverbTime = binaryReader.ReadByte();
            reverb.PianoReverbDepth = binaryReader.ReadByte();
            reverb.EPianoReverbDepth = binaryReader.ReadByte();
            reverb.SubReverbDepth = binaryReader.ReadByte();

            return reverb;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.WriteBigEndian(0x5U);
            binaryWriter.Write(ReverbOnOff);
            binaryWriter.Write(ReverbTime);
            binaryWriter.Write(PianoReverbDepth);
            binaryWriter.Write(EPianoReverbDepth);
            binaryWriter.Write(SubReverbDepth);
        }
    }

    [Serializable]
    public record MasterKeyboardZone
    {
        public byte ZoneSwitchOnOff;
        public byte TxChannel;
        public byte OctaveShift;
        public byte Transpose;
        public byte NoteLimitLow;
        public byte NoteLimitHigh;
        public byte TxSWNote;
        public byte TxSWBank;
        public byte TxSWProgram;
        public byte TxSWVolume;
        public byte TxSWPan;
        public byte TxSWPB;
        public byte TxSWMod;
        public byte TxSWFC1;
        public byte TxSWFC2;
        public byte TxSWFS;
        public byte TxSWSustain;
        public byte BankMsb;
        public byte BankLsb;
        public byte ProgramChange;
        public byte Volume;
        public byte Pan;

        public static MasterKeyboardZone Read(BinaryReader binaryReader)
        {
            MasterKeyboardZone zone = new MasterKeyboardZone();

            binaryReader.ExpectBigEndianUInt32(0x16);
            zone.ZoneSwitchOnOff = binaryReader.ReadByte();
            zone.TxChannel = binaryReader.ReadByte();
            zone.OctaveShift = binaryReader.ReadByte();
            zone.Transpose = binaryReader.ReadByte();
            zone.NoteLimitLow = binaryReader.ReadByte();
            zone.NoteLimitHigh = binaryReader.ReadByte();
            zone.TxSWNote = binaryReader.ReadByte();
            zone.TxSWBank = binaryReader.ReadByte();
            zone.TxSWProgram = binaryReader.ReadByte();
            zone.TxSWVolume = binaryReader.ReadByte();
            zone.TxSWPan = binaryReader.ReadByte();
            zone.TxSWPB = binaryReader.ReadByte();
            zone.TxSWMod = binaryReader.ReadByte();
            zone.TxSWFC1 = binaryReader.ReadByte();
            zone.TxSWFC2 = binaryReader.ReadByte();
            zone.TxSWFS = binaryReader.ReadByte();
            zone.TxSWSustain = binaryReader.ReadByte();
            zone.BankMsb = binaryReader.ReadByte();
            zone.BankLsb = binaryReader.ReadByte();
            zone.ProgramChange = binaryReader.ReadByte();
            zone.Volume = binaryReader.ReadByte();
            zone.Pan = binaryReader.ReadByte();

            return zone;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.WriteBigEndian(0x16U);
            binaryWriter.Write(ZoneSwitchOnOff);
            binaryWriter.Write(TxChannel);
            binaryWriter.Write(OctaveShift);
            binaryWriter.Write(Transpose);
            binaryWriter.Write(NoteLimitLow);
            binaryWriter.Write(NoteLimitHigh);
            binaryWriter.Write(TxSWNote);
            binaryWriter.Write(TxSWBank);
            binaryWriter.Write(TxSWProgram);
            binaryWriter.Write(TxSWVolume);
            binaryWriter.Write(TxSWPan);
            binaryWriter.Write(TxSWPB);
            binaryWriter.Write(TxSWMod);
            binaryWriter.Write(TxSWFC1);
            binaryWriter.Write(TxSWFC2);
            binaryWriter.Write(TxSWFS);
            binaryWriter.Write(TxSWSustain);
            binaryWriter.Write(BankMsb);
            binaryWriter.Write(BankLsb);
            binaryWriter.Write(ProgramChange);
            binaryWriter.Write(Volume);
            binaryWriter.Write(Pan);
        }
    }

    [Serializable]
    public record Section
    {
        public byte VoiceCategory;
        public byte VoiceNumberCategory1;
        public byte VoiceNumberCategory2;
        public byte VoiceNumberCategory3;
        public byte VoiceNumberCategory4;
        public byte VoiceAdvancedModeNumber;
        public byte OnOff;
        public byte Split;
        public byte Octave;
        public byte Volume;
        public byte Tone;
        public byte PitchBendRange;
        public byte PModDepth;
        public byte RxSwitchExpression;
        public byte RxSwitchSustain;
        public byte RxSwitchSostenuto;
        public byte RxSwitchSoft;
        public byte DelayDepth;
        public byte ReverbDepth;
        public byte AdvancedModeSwitchOnOff;
        public byte PModSpeed;
        public SectionExtension? Extension;
        public SectionExtension2? Extension2;

        public byte PianoDamperResonance;
        public byte PianoDspOnOff;
        public byte PianoDspCategory;
        public byte PianoDspDepth;
        public byte EPianoDsp1OnOff;
        public byte EPianoDsp1Category;
        public byte EPianoDsp1Depth;
        public byte EPianoDsp1Rate;
        public byte EPianoDsp2OnOff;
        public byte EPianoDsp2Category;
        public byte EPianoDsp2Depth;
        public byte EPianoDsp2Speed;
        public byte EPianoDriveOnOff;
        public byte EPianoDriveValue;
        public byte SubDspOnOff;
        public byte SubDspCategory;
        public byte SubDspDepth;
        public byte SubDspSpeed;
        public byte SubDspAttack;
        public byte SubDspRelease;

        public static Section Read(BinaryReader binaryReader)
        {
            Section section = new Section();

            uint structLength = binaryReader.ReadBigEndianUInt32();
            if (structLength != 0x15 && structLength != 0x17 && structLength != 0x1D)
                throw new InvalidDataException();
            section.VoiceCategory = binaryReader.ReadByte();
            section.VoiceNumberCategory1 = binaryReader.ReadByte();
            section.VoiceNumberCategory2 = binaryReader.ReadByte();
            section.VoiceNumberCategory3 = binaryReader.ReadByte();
            section.VoiceNumberCategory4 = binaryReader.ReadByte();
            section.VoiceAdvancedModeNumber = binaryReader.ReadByte();
            section.OnOff = binaryReader.ReadByte();
            section.Split = binaryReader.ReadByte();
            section.Octave = binaryReader.ReadByte();
            section.Volume = binaryReader.ReadByte();
            section.Tone = binaryReader.ReadByte();
            section.PitchBendRange = binaryReader.ReadByte();
            section.PModDepth = binaryReader.ReadByte();
            section.RxSwitchExpression = binaryReader.ReadByte();
            section.RxSwitchSustain = binaryReader.ReadByte();
            section.RxSwitchSostenuto = binaryReader.ReadByte();
            section.RxSwitchSoft = binaryReader.ReadByte();
            section.DelayDepth = binaryReader.ReadByte();
            section.ReverbDepth = binaryReader.ReadByte();
            section.AdvancedModeSwitchOnOff = binaryReader.ReadByte();
            section.PModSpeed = binaryReader.ReadByte();
            if (structLength >= 0x17)
                section.Extension = SectionExtension.Read(binaryReader);
            if (structLength >= 0x1D)
                section.Extension2 = SectionExtension2.Read(binaryReader);

            binaryReader.ExpectBigEndianUInt32(0x14);
            section.PianoDamperResonance = binaryReader.ReadByte();
            section.PianoDspOnOff = binaryReader.ReadByte();
            section.PianoDspCategory = binaryReader.ReadByte();
            section.PianoDspDepth = binaryReader.ReadByte();
            section.EPianoDsp1OnOff = binaryReader.ReadByte();
            section.EPianoDsp1Category = binaryReader.ReadByte();
            section.EPianoDsp1Depth = binaryReader.ReadByte();
            section.EPianoDsp1Rate = binaryReader.ReadByte();
            section.EPianoDsp2OnOff = binaryReader.ReadByte();
            section.EPianoDsp2Category = binaryReader.ReadByte();
            section.EPianoDsp2Depth = binaryReader.ReadByte();
            section.EPianoDsp2Speed = binaryReader.ReadByte();
            section.EPianoDriveOnOff = binaryReader.ReadByte();
            section.EPianoDriveValue = binaryReader.ReadByte();
            section.SubDspOnOff = binaryReader.ReadByte();
            section.SubDspCategory = binaryReader.ReadByte();
            section.SubDspDepth = binaryReader.ReadByte();
            section.SubDspSpeed = binaryReader.ReadByte();
            section.SubDspAttack = binaryReader.ReadByte();
            section.SubDspRelease = binaryReader.ReadByte();

            return section;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            uint structLength = (Extension, Extension2) switch
            {
                (null, null) => 0x15U,
                (not null, null) => 0x17U,
                (not null, not null) => 0x1DU,
                _ => throw new InvalidOperationException($"If {nameof(Extension2)} is set, {nameof(Extension)} must also be set.")
            };
            binaryWriter.WriteBigEndian(structLength);
            binaryWriter.Write(VoiceCategory);
            binaryWriter.Write(VoiceNumberCategory1);
            binaryWriter.Write(VoiceNumberCategory2);
            binaryWriter.Write(VoiceNumberCategory3);
            binaryWriter.Write(VoiceNumberCategory4);
            binaryWriter.Write(VoiceAdvancedModeNumber);
            binaryWriter.Write(OnOff);
            binaryWriter.Write(Split);
            binaryWriter.Write(Octave);
            binaryWriter.Write(Volume);
            binaryWriter.Write(Tone);
            binaryWriter.Write(PitchBendRange);
            binaryWriter.Write(PModDepth);
            binaryWriter.Write(RxSwitchExpression);
            binaryWriter.Write(RxSwitchSustain);
            binaryWriter.Write(RxSwitchSostenuto);
            binaryWriter.Write(RxSwitchSoft);
            binaryWriter.Write(DelayDepth);
            binaryWriter.Write(ReverbDepth);
            binaryWriter.Write(AdvancedModeSwitchOnOff);
            binaryWriter.Write(PModSpeed);
            Extension?.WriteTo(binaryWriter);
            Extension2?.WriteTo(binaryWriter);

            binaryWriter.WriteBigEndian(0x14U);
            binaryWriter.Write(PianoDamperResonance);
            binaryWriter.Write(PianoDspOnOff);
            binaryWriter.Write(PianoDspCategory);
            binaryWriter.Write(PianoDspDepth);
            binaryWriter.Write(EPianoDsp1OnOff);
            binaryWriter.Write(EPianoDsp1Category);
            binaryWriter.Write(EPianoDsp1Depth);
            binaryWriter.Write(EPianoDsp1Rate);
            binaryWriter.Write(EPianoDsp2OnOff);
            binaryWriter.Write(EPianoDsp2Category);
            binaryWriter.Write(EPianoDsp2Depth);
            binaryWriter.Write(EPianoDsp2Speed);
            binaryWriter.Write(EPianoDriveOnOff);
            binaryWriter.Write(EPianoDriveValue);
            binaryWriter.Write(SubDspOnOff);
            binaryWriter.Write(SubDspCategory);
            binaryWriter.Write(SubDspDepth);
            binaryWriter.Write(SubDspSpeed);
            binaryWriter.Write(SubDspAttack);
            binaryWriter.Write(SubDspRelease);
        }
    }

    [Serializable]
    public record SectionExtension
    {
        public byte TouchSensitivityDepth;
        public byte TouchSensitivityOffset;

        public static SectionExtension Read(BinaryReader binaryReader)
        {
            SectionExtension sectionExtension = new SectionExtension();

            sectionExtension.TouchSensitivityDepth = binaryReader.ReadByte();
            sectionExtension.TouchSensitivityOffset = binaryReader.ReadByte();

            return sectionExtension;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(TouchSensitivityDepth);
            binaryWriter.Write(TouchSensitivityOffset);
        }

        public static SectionExtension Default => new()
        {
            TouchSensitivityDepth = 64,
            TouchSensitivityOffset = 64
        };
    }

    [Serializable]
    public record SectionExtension2
    {
        public byte SoundMonoPoly;
        public byte SoundPortamentoSwitch;
        public byte SoundPortamentoTime;
        public byte SoundPortamentoMode;
        public byte SoundPortamentoTimeMode;
        public byte SoundPan;

        public static SectionExtension2 Read(BinaryReader binaryReader)
        {
            SectionExtension2 sectionExtension2 = new SectionExtension2();

            sectionExtension2.SoundMonoPoly = binaryReader.ReadByte();
            sectionExtension2.SoundPortamentoSwitch = binaryReader.ReadByte();
            sectionExtension2.SoundPortamentoTime = binaryReader.ReadByte();
            sectionExtension2.SoundPortamentoMode = binaryReader.ReadByte();
            sectionExtension2.SoundPortamentoTimeMode = binaryReader.ReadByte();
            sectionExtension2.SoundPan = binaryReader.ReadByte();

            return sectionExtension2;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.Write(SoundMonoPoly);
            binaryWriter.Write(SoundPortamentoSwitch);
            binaryWriter.Write(SoundPortamentoTime);
            binaryWriter.Write(SoundPortamentoMode);
            binaryWriter.Write(SoundPortamentoTimeMode);
            binaryWriter.Write(SoundPan);
        }

        public static SectionExtension2 Default => new()
        {
            SoundMonoPoly = 1,
            SoundPortamentoSwitch = 0,
            SoundPortamentoTime = 64,
            SoundPortamentoMode = 1,
            SoundPortamentoTimeMode = 0,
            SoundPan = 64
        };
    }

    [Serializable]
    public record LiveSetEQ2
    {
        public byte LowGain;
        public byte MidGain;
        public byte MidGainFrequency;
        public byte HighGain;

        public static LiveSetEQ2 Read(BinaryReader binaryReader)
        {
            LiveSetEQ2 liveSetEQ2 = new LiveSetEQ2();

            binaryReader.ExpectBigEndianUInt32(0x4);
            liveSetEQ2.LowGain = binaryReader.ReadByte();
            liveSetEQ2.MidGain = binaryReader.ReadByte();
            liveSetEQ2.MidGainFrequency = binaryReader.ReadByte();
            liveSetEQ2.HighGain = binaryReader.ReadByte();

            return liveSetEQ2;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.WriteBigEndian(0x4U);
            binaryWriter.Write(LowGain);
            binaryWriter.Write(MidGain);
            binaryWriter.Write(MidGainFrequency);
            binaryWriter.Write(HighGain);
        }

        public static LiveSetEQ2 Default => new()
        {
            LowGain = 64,
            MidGain = 64,
            MidGainFrequency = 64,
            HighGain = 64
        };
}

    [Serializable]
    public record SystemData
    {
        public byte AutoPowerOff;
        public byte KeyboardOctave;
        public byte Transpose;
        public byte LocalControl;
        public byte MidiTxChannel;
        public byte MidiRxChannel;
        public byte MidiControl;
        public byte Unknown1;
        public byte TouchCurve;
        public byte FixedVelocity;
        public byte TxRxBankSelect;
        public byte TxRxPrgmChange;
        public byte MidiPortMidiInOut;
        public byte MidiPortUsbInOut;
        public byte DisplayLightsInsEffect;
        public byte DisplayLightsSection;
        public byte DisplayLightsLcdSwitch;
        public byte ValueIndication;
        public byte SwitchDirection;
        public byte LcdContrast;
        public byte PanelLockLiveSet;
        public byte PanelLockPianoEPianoSub;
        public byte PanelLockDelayReverb;
        public byte PanelLockMasterEQ;
        public byte SectionHold;
        public byte LiveSetViewMode;
        public byte FootSwitchAssign;
        public byte SustainPedalType;
        public byte PowerOnSoundLiveSetPage;
        public byte PowerOnSoundLiveSetIndex;
        public byte ControllerReset;
        public byte UsbAudioVolume;
        public byte MidiDeviceNumber;
        public byte MidiControlDelay;

        public ushort MasterTune;
        public byte Unknown2;
        public byte Unknown3;

        public static SystemData Read(BinaryReader binaryReader)
        {
            SystemData systemData = new SystemData();

            binaryReader.ExpectBigEndianUInt32(0x22);
            systemData.AutoPowerOff = binaryReader.ReadByte();
            systemData.KeyboardOctave = binaryReader.ReadByte();
            systemData.Transpose = binaryReader.ReadByte();
            systemData.LocalControl = binaryReader.ReadByte();
            systemData.MidiTxChannel = binaryReader.ReadByte();
            systemData.MidiRxChannel = binaryReader.ReadByte();
            systemData.MidiControl = binaryReader.ReadByte();
            systemData.Unknown1 = binaryReader.ReadByte();
            systemData.TouchCurve = binaryReader.ReadByte();
            systemData.FixedVelocity = binaryReader.ReadByte();
            systemData.TxRxBankSelect = binaryReader.ReadByte();
            systemData.TxRxPrgmChange = binaryReader.ReadByte();
            systemData.MidiPortMidiInOut = binaryReader.ReadByte();
            systemData.MidiPortUsbInOut = binaryReader.ReadByte();
            systemData.DisplayLightsInsEffect = binaryReader.ReadByte();
            systemData.DisplayLightsSection = binaryReader.ReadByte();
            systemData.DisplayLightsLcdSwitch = binaryReader.ReadByte();
            systemData.ValueIndication = binaryReader.ReadByte();
            systemData.SwitchDirection = binaryReader.ReadByte();
            systemData.LcdContrast = binaryReader.ReadByte();
            systemData.PanelLockLiveSet = binaryReader.ReadByte();
            systemData.PanelLockPianoEPianoSub = binaryReader.ReadByte();
            systemData.PanelLockDelayReverb = binaryReader.ReadByte();
            systemData.PanelLockMasterEQ = binaryReader.ReadByte();
            systemData.SectionHold = binaryReader.ReadByte();
            systemData.LiveSetViewMode = binaryReader.ReadByte();
            systemData.FootSwitchAssign = binaryReader.ReadByte();
            systemData.SustainPedalType = binaryReader.ReadByte();
            systemData.PowerOnSoundLiveSetPage = binaryReader.ReadByte();
            systemData.PowerOnSoundLiveSetIndex = binaryReader.ReadByte();
            systemData.ControllerReset = binaryReader.ReadByte();
            systemData.UsbAudioVolume = binaryReader.ReadByte();
            systemData.MidiDeviceNumber = binaryReader.ReadByte();
            systemData.MidiControlDelay = binaryReader.ReadByte();

            binaryReader.ExpectBigEndianUInt32(0x4);
            systemData.MasterTune = binaryReader.ReadUInt16();
            systemData.Unknown2 = binaryReader.ReadByte();
            systemData.Unknown3 = binaryReader.ReadByte();

            return systemData;
        }

        public void WriteTo(BinaryWriter binaryWriter)
        {
            binaryWriter.WriteBigEndian(0x22U);
            binaryWriter.Write(AutoPowerOff);
            binaryWriter.Write(KeyboardOctave);
            binaryWriter.Write(Transpose);
            binaryWriter.Write(LocalControl);
            binaryWriter.Write(MidiTxChannel);
            binaryWriter.Write(MidiRxChannel);
            binaryWriter.Write(MidiControl);
            binaryWriter.Write(Unknown1);
            binaryWriter.Write(TouchCurve);
            binaryWriter.Write(FixedVelocity);
            binaryWriter.Write(TxRxBankSelect);
            binaryWriter.Write(TxRxPrgmChange);
            binaryWriter.Write(MidiPortMidiInOut);
            binaryWriter.Write(MidiPortUsbInOut);
            binaryWriter.Write(DisplayLightsInsEffect);
            binaryWriter.Write(DisplayLightsSection);
            binaryWriter.Write(DisplayLightsLcdSwitch);
            binaryWriter.Write(ValueIndication);
            binaryWriter.Write(SwitchDirection);
            binaryWriter.Write(LcdContrast);
            binaryWriter.Write(PanelLockLiveSet);
            binaryWriter.Write(PanelLockPianoEPianoSub);
            binaryWriter.Write(PanelLockDelayReverb);
            binaryWriter.Write(PanelLockMasterEQ);
            binaryWriter.Write(SectionHold);
            binaryWriter.Write(LiveSetViewMode);
            binaryWriter.Write(FootSwitchAssign);
            binaryWriter.Write(SustainPedalType);
            binaryWriter.Write(PowerOnSoundLiveSetPage);
            binaryWriter.Write(PowerOnSoundLiveSetIndex);
            binaryWriter.Write(ControllerReset);
            binaryWriter.Write(UsbAudioVolume);
            binaryWriter.Write(MidiDeviceNumber);
            binaryWriter.Write(MidiControlDelay);

            binaryWriter.WriteBigEndian(0x4U);
            binaryWriter.Write(MasterTune);
            binaryWriter.Write(Unknown2);
            binaryWriter.Write(Unknown3);
        }
    }

    private class YamahaEncoding : Encoding
    {
        public override int GetByteCount(char[] chars, int index, int count)
        {
            return Encoding.ASCII.GetByteCount(chars, index, count);
        }

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            char[] charsTmp = new char[charCount];
            Array.Copy(chars, charIndex, charsTmp, 0, charCount);

            charsTmp.AsSpan().Replace('¥', '\\');

            int byteCount = Encoding.ASCII.GetBytes(charsTmp, 0, charCount, bytes, byteIndex);
            return byteCount;
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            return Encoding.ASCII.GetCharCount(bytes, index, count);
        }

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            int charCount = Encoding.ASCII.GetChars(bytes, byteIndex, byteCount, chars, charIndex);

            chars.AsSpan(charIndex, charCount).Replace('\\', '¥');

            return charCount;
        }

        public override int GetMaxByteCount(int charCount)
        {
            return Encoding.ASCII.GetMaxByteCount(charCount);
        }

        public override int GetMaxCharCount(int byteCount)
        {
            return Encoding.ASCII.GetMaxCharCount(byteCount);
        }
    }
}    
