using System;
using System.Collections.Generic;

namespace TRProtocol
{
	public sealed class PeFile
	{
		public struct Section
		{
			public string Name;
			public int Va;
			public int Vsz;
			public int Raw;
			public int Rsz;
		}

		public bool Is64Bit;
		public long ImageBase;
		public readonly List<Section> Sections = new List<Section>();

		public int PtrSize
		{
			get { return Is64Bit ? 8 : 4; }
		}

		public static PeFile Parse(byte[] d)
		{
			PeFile pe = new PeFile();
			if (d.Length < 64 || d[0] != (byte)'M' || d[1] != (byte)'Z')
			{
				throw new InvalidOperationException("Not a PE file.");
			}
			int lfanew = BitConverter.ToInt32(d, 60);
			if (lfanew < 0 || lfanew + 24 >= d.Length || d[lfanew] != (byte)'P' || d[lfanew + 1] != (byte)'E')
			{
				throw new InvalidOperationException("Invalid PE header.");
			}
			int nsec = BitConverter.ToUInt16(d, lfanew + 6);
			int optSize = BitConverter.ToUInt16(d, lfanew + 20);
			int opt = lfanew + 24;
			int magic = BitConverter.ToUInt16(d, opt);
			pe.Is64Bit = magic == 0x20B;
			pe.ImageBase = pe.Is64Bit ? BitConverter.ToInt64(d, opt + 24) : BitConverter.ToUInt32(d, opt + 28);
			int sec = opt + optSize;
			for (int i = 0; i < nsec; i++)
			{
				int o = sec + i * 40;
				if (o + 40 > d.Length)
				{
					break;
				}
				Section s = new Section();
				s.Name = ReadSectionName(d, o);
				s.Vsz = BitConverter.ToInt32(d, o + 8);
				s.Va = BitConverter.ToInt32(d, o + 12);
				s.Rsz = BitConverter.ToInt32(d, o + 16);
				s.Raw = BitConverter.ToInt32(d, o + 20);
				pe.Sections.Add(s);
			}
			return pe;
		}

		public Section? Text()
		{
			for (int i = 0; i < Sections.Count; i++)
			{
				if (Sections[i].Name == ".text")
				{
					return Sections[i];
				}
			}
			return null;
		}

		public byte[] PointerBytes(long va)
		{
			if (Is64Bit)
			{
				return BitConverter.GetBytes(va);
			}
			return BitConverter.GetBytes((uint)va);
		}

		public long ReadPointer(byte[] image, int off)
		{
			if (Is64Bit)
			{
				return BitConverter.ToInt64(image, off);
			}
			return BitConverter.ToUInt32(image, off);
		}

		public long OffsetToVa(int off)
		{
			for (int i = 0; i < Sections.Count; i++)
			{
				Section s = Sections[i];
				if (off >= s.Raw && off < s.Raw + s.Rsz)
				{
					return ImageBase + s.Va + (off - s.Raw);
				}
			}
			return 0L;
		}

		public int VaToOffset(long va)
		{
			long rva = va - ImageBase;
			if (rva < 0 || rva > int.MaxValue)
			{
				return -1;
			}
			return RvaToOffset((int)rva);
		}

		public int RvaToOffset(int rva)
		{
			for (int i = 0; i < Sections.Count; i++)
			{
				Section s = Sections[i];
				int span = s.Vsz > s.Rsz ? s.Vsz : s.Rsz;
				if (rva >= s.Va && rva < s.Va + span)
				{
					int off = s.Raw + (rva - s.Va);
					if (off >= s.Raw && off < s.Raw + s.Rsz)
					{
						return off;
					}
					return -1;
				}
			}
			return -1;
		}

		public bool InText(int rva)
		{
			Section? text = Text();
			if (text == null)
			{
				return false;
			}
			Section s = text.Value;
			return rva >= s.Va && rva < s.Va + s.Vsz;
		}

		private static string ReadSectionName(byte[] d, int o)
		{
			int n = 0;
			while (n < 8 && d[o + n] != 0)
			{
				n++;
			}
			return System.Text.Encoding.ASCII.GetString(d, o, n);
		}
	}
}
