using System;
using System.IO;
using System.Text;

namespace TRProtocol
{
	public sealed class DumpResult
	{
		public string CsvText;
		public string CsvPath;
		public string Status;
		public int Rows;
	}

	public static class OpcodeDump
	{
		private static readonly string[] Markers = new string[3] { "eServer_DUMMY", "eServer_LOGIN_REQ", "eServer_LOGIN_OK_ACK" };

		public static DumpResult DumpFile(string path)
		{
			byte[] image = File.ReadAllBytes(path);
			PeFile pe = PeFile.Parse(image);
			int[] strides = pe.Is64Bit ? new int[1] { 16 } : new int[3] { 8, 16, 4 };
			int tableOff;
			int stride;
			if (!FindNameTable(image, pe, strides, out tableOff, out stride) && !pe.Is64Bit)
			{
				FindFromAob32(image, pe, out tableOff, out stride);
			}
			if (tableOff < 0)
			{
				throw new InvalidOperationException("Opcode name table was not found in " + Path.GetFileName(path) + ".");
			}

			string[] names = ReadNames(image, pe, tableOff, stride);
			if (names.Length < 32)
			{
				throw new InvalidOperationException("Opcode table looked too small in " + Path.GetFileName(path) + ".");
			}

			string wireNote;
			string[] wires = WireDump.Collect(image, pe, names.Length, out wireNote);
			StringBuilder csv = new StringBuilder();
			csv.AppendLine("Opcode,Name,Wire");
			int rows = 0;
			for (int i = 0; i < names.Length; i++)
			{
				if (string.IsNullOrEmpty(names[i]))
				{
					continue;
				}
				csv.Append(i.ToString());
				csv.Append(',');
				csv.Append(names[i]);
				csv.Append(',');
				if (wires != null && i < wires.Length && !string.IsNullOrEmpty(wires[i]))
				{
					csv.Append(wires[i]);
				}
				csv.AppendLine();
				rows++;
			}

			string stem = Path.GetFileNameWithoutExtension(path);
			if (string.IsNullOrEmpty(stem))
			{
				stem = "opcodes";
			}
			string dir = Path.GetDirectoryName(Path.GetFullPath(path));
			string csvPath = Path.Combine(dir, stem + "_opcode.csv");
			File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

			DumpResult result = new DumpResult();
			result.CsvText = csv.ToString();
			result.CsvPath = csvPath;
			result.Rows = rows;
			result.Status = string.IsNullOrEmpty(wireNote)
				? "Dumped " + rows + " opcodes."
				: "Dumped " + rows + " opcodes. " + wireNote;
			return result;
		}

		private static string[] ReadNames(byte[] image, PeFile pe, int tableOff, int stride)
		{
			string[] names = new string[4096];
			int count = 0;
			string name;
			for (int i = 0; i < names.Length && TryReadName(image, pe, tableOff + i * stride, out name); i++)
			{
				if (IsProtocolName(name))
				{
					names[i] = name;
					count++;
				}
			}
			if (count < 32)
			{
				return new string[0];
			}
			int last = names.Length;
			while (last > 0 && string.IsNullOrEmpty(names[last - 1]))
			{
				last--;
			}
			string[] trimmed = new string[last];
			Array.Copy(names, trimmed, last);
			return trimmed;
		}

		private static bool IsProtocolName(string name)
		{
			return name.StartsWith("eServer_", StringComparison.Ordinal)
				|| name.StartsWith("eRoom_", StringComparison.Ordinal)
				|| name.StartsWith("eRelay", StringComparison.Ordinal)
				|| name.StartsWith("eUDP", StringComparison.Ordinal)
				|| name.StartsWith("eFarm", StringComparison.Ordinal)
				|| name.StartsWith("eMyRoom", StringComparison.Ordinal);
		}

		private static bool FindNameTable(byte[] image, PeFile pe, int[] strides, out int tableOff, out int stride)
		{
			tableOff = -1;
			stride = strides[0];
			int best = 0;
			foreach (int step in strides)
			{
				int align = pe.PtrSize - 1;
				for (int j = 0; j < Markers.Length; j++)
				{
					byte[] marker = Encoding.ASCII.GetBytes(Markers[j] + "\0");
					int start = 0;
					while (true)
					{
						int strOff = IndexOf(image, marker, start);
						if (strOff < 0)
						{
							break;
						}
						start = strOff + 1;
						long va = pe.OffsetToVa(strOff);
						if (va == 0)
						{
							continue;
						}
						byte[] needle = pe.PointerBytes(va);
						int start2 = 0;
						while (true)
						{
							int ptrOff = IndexOf(image, needle, start2);
							if (ptrOff < 0)
							{
								break;
							}
							start2 = ptrOff + 1;
							if ((ptrOff & align) != 0)
							{
								continue;
							}
							int slot = ptrOff;
							string dummy;
							if (j > 0 && TryReadName(image, pe, ptrOff - step, out dummy) && dummy.IndexOf("DUMMY", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								slot = ptrOff - step;
							}
							int n = CountEntries(image, pe, slot, step);
							if (n > best)
							{
								best = n;
								tableOff = slot;
								stride = step;
							}
						}
					}
				}
			}
			return best >= 32;
		}

		private static void FindFromAob32(byte[] image, PeFile pe, out int tableOff, out int stride)
		{
			tableOff = -1;
			stride = 8;
			int[] pattern = ParsePattern("C7 45 FC 00 00 00 00 81 FE ?? ?? 00 00 77 09 8B 14 F5 ?? ?? ?? ??");
			int best = 0;
			int last = image.Length - pattern.Length;
			for (int i = 0; i <= last; i++)
			{
				if (!Match(image, i, pattern))
				{
					continue;
				}
				int countImm = BitConverter.ToInt32(image, i + 9);
				if (countImm < 32 || countImm > 4096)
				{
					continue;
				}
				int tableVa = BitConverter.ToInt32(image, i + 18);
				int off = pe.VaToOffset((uint)tableVa);
				if (off < 0)
				{
					continue;
				}
				int n = CountEntries(image, pe, off, 8);
				if (n > best)
				{
					best = n;
					tableOff = off;
				}
			}
			if (best < 32)
			{
				tableOff = -1;
			}
		}

		private static int CountEntries(byte[] image, PeFile pe, int tableOff, int stride)
		{
			int n = 0;
			string name;
			for (int i = 0; i < 4096 && TryReadName(image, pe, tableOff + i * stride, out name); i++)
			{
				n++;
			}
			return n;
		}

		private static bool TryReadName(byte[] image, PeFile pe, int off, out string name)
		{
			name = null;
			if (off < 0)
			{
				return false;
			}
			int ptrSize = pe.PtrSize;
			if (off + ptrSize > image.Length)
			{
				return false;
			}
			long va = pe.ReadPointer(image, off);
			int str = pe.VaToOffset(va);
			if (str < 0)
			{
				return false;
			}
			int len = 0;
			if (off + ptrSize + 4 <= image.Length)
			{
				int stored = BitConverter.ToInt32(image, off + ptrSize);
				if (stored >= 3 && stored <= 200 && str + stored <= image.Length && IsName(image, str, stored))
				{
					len = stored;
				}
			}
			if (len == 0)
			{
				len = CStringLength(image, str, 200);
				if (len < 3 || !IsName(image, str, len))
				{
					return false;
				}
			}
			name = Encoding.ASCII.GetString(image, str, len);
			return true;
		}

		private static int[] ParsePattern(string pattern)
		{
			string[] parts = pattern.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			int[] bytes = new int[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				bytes[i] = parts[i] == "??" ? -1 : Convert.ToByte(parts[i], 16);
			}
			return bytes;
		}

		private static bool Match(byte[] image, int index, int[] pattern)
		{
			for (int i = 0; i < pattern.Length; i++)
			{
				if (pattern[i] >= 0 && image[index + i] != pattern[i])
				{
					return false;
				}
			}
			return true;
		}

		private static int CStringLength(byte[] image, int off, int maxLen)
		{
			int n = image.Length - off;
			if (n > maxLen)
			{
				n = maxLen;
			}
			int i = 0;
			for (; i < n && image[off + i] != 0; i++)
			{
			}
			return i;
		}

		private static bool IsName(byte[] image, int off, int len)
		{
			if (len < 3)
			{
				return false;
			}
			byte b = image[off];
			if ((b < (byte)'A' || b > (byte)'Z') && (b < (byte)'a' || b > (byte)'z') && b != (byte)'_')
			{
				return false;
			}
			for (int i = 0; i < len; i++)
			{
				byte c = image[off + i];
				if (c < 32 || c > 126)
				{
					return false;
				}
			}
			return true;
		}

		private static int IndexOf(byte[] hay, byte[] needle, int start)
		{
			int last = hay.Length - needle.Length;
			for (int i = start; i <= last; i++)
			{
				bool ok = true;
				for (int j = 0; j < needle.Length; j++)
				{
					if (hay[i + j] != needle[j])
					{
						ok = false;
						break;
					}
				}
				if (ok)
				{
					return i;
				}
			}
			return -1;
		}
	}
}
