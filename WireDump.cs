using System;
using System.Collections.Generic;
using Iced.Intel;

namespace TRProtocol
{
	/// <summary>
	/// Recv-switch pop sizes. A straight dump stops at the first branch,
	/// so the wire column is the fixed prefix (b4, b2, b1, b8, str16).
	/// </summary>
	public static class WireDump
	{
		private const int MinTable = 800;

		public static string[] Collect(byte[] image, PeFile pe, int opcodeCount, out string note)
		{
			note = "";
			string[] wires = new string[opcodeCount];
			if (!pe.Is64Bit)
			{
				note = "wire skipped (32-bit client)";
				return wires;
			}
			PeFile.Section? textOpt = pe.Text();
			if (textOpt == null)
			{
				note = "wire skipped (no .text)";
				return wires;
			}
			PeFile.Section text = textOpt.Value;
			if (IsPacked(image, text))
			{
				note = "wire skipped (packed .text — dump an unpacked exe)";
				return wires;
			}

			int pop = FindPop(image, pe, text);
			int table = FindSwitchTable(image, pe, text);
			if (pop == 0 || table < 0)
			{
				note = "wire skipped (recv switch not found)";
				return wires;
			}

			int tableOff = pe.RvaToOffset(table);
			if (tableOff < 0)
			{
				note = "wire skipped (switch table is not in the file)";
				return wires;
			}

			int filled = 0;
			int slots = 0;
			for (int op = 2; op < opcodeCount; op++)
			{
				int pos = tableOff + (op - 2) * 4;
				if (pos + 4 > image.Length)
				{
					break;
				}
				int handler = BitConverter.ToInt32(image, pos);
				if (!pe.InText(handler))
				{
					break;
				}
				slots++;
				string wire = Walk(image, pe, handler, pop, 1, 80);
				wires[op] = wire ?? "";
				if (!string.IsNullOrEmpty(wire))
				{
					filled++;
				}
			}
			note = "wire " + filled + "/" + slots + "  pop=0x" + pop.ToString("X") + "  table=0x" + table.ToString("X");
			return wires;
		}

		private static bool IsPacked(byte[] image, PeFile.Section text)
		{
			if (text.Rsz <= 0 || text.Raw < 0 || text.Raw >= image.Length)
			{
				return true;
			}
			if (text.Vsz > text.Rsz * 2)
			{
				return true;
			}
			int sample = 0x8000;
			if (text.Rsz < sample * 3)
			{
				return Entropy(image, text.Raw, Math.Min(sample, text.Rsz)) > 7.2;
			}
			double a = Entropy(image, text.Raw, sample);
			double b = Entropy(image, text.Raw + (text.Rsz / 2), sample);
			double c = Entropy(image, text.Raw + text.Rsz - sample, sample);
			return a > 7.2 && b > 7.2 && c > 7.2;
		}

		private static double Entropy(byte[] d, int off, int len)
		{
			int[] count = new int[256];
			for (int i = 0; i < len; i++)
			{
				count[d[off + i]]++;
			}
			double e = 0;
			for (int i = 0; i < 256; i++)
			{
				if (count[i] == 0)
				{
					continue;
				}
				double p = count[i] / (double)len;
				e -= p * Math.Log(p, 2);
			}
			return e;
		}

		private static int FindPop(byte[] image, PeFile pe, PeFile.Section text)
		{
			Dictionary<int, int> hits = new Dictionary<int, int>();
			int end = text.Raw + text.Rsz - 11;
			for (int i = text.Raw; i < end; i++)
			{
				if (image[i] != 0x41 || image[i + 1] != 0xB8)
				{
					continue;
				}
				byte branch = image[i + 6];
				if (branch != 0xE8 && branch != 0xE9)
				{
					continue;
				}
				int imm = BitConverter.ToInt32(image, i + 2);
				if (imm != 1 && imm != 2 && imm != 4 && imm != 8 && imm != 12 && imm != 16)
				{
					continue;
				}
				int rel = BitConverter.ToInt32(image, i + 7);
				int insnRva = text.Va + (i - text.Raw);
				int target = insnRva + 11 + rel;
				if (!pe.InText(target))
				{
					continue;
				}
				int n;
				hits.TryGetValue(target, out n);
				hits[target] = n + 1;
			}
			int best = 0;
			int bestN = 0;
			foreach (KeyValuePair<int, int> kv in hits)
			{
				if (kv.Value > bestN)
				{
					bestN = kv.Value;
					best = kv.Key;
				}
			}
			return bestN >= 8 ? best : 0;
		}

		private static int FindSwitchTable(byte[] image, PeFile pe, PeFile.Section text)
		{
			int bestTable = -1;
			int bestCount = 0;
			int end = text.Raw + text.Rsz - 16;
			for (int i = text.Raw; i < end; i++)
			{
				// mov eax, [rbx+rax*4+disp32]; add rax, rbx; jmp rax
				if (image[i] != 0x8B || image[i + 1] != 0x84 || image[i + 2] != 0x83)
				{
					continue;
				}
				if (image[i + 7] != 0x48 || image[i + 8] != 0x03 || image[i + 9] != 0xC3 || image[i + 10] != 0xFF || image[i + 11] != 0xE0)
				{
					continue;
				}
				if (!RbxIsImageBase(image, pe, text, i))
				{
					continue;
				}
				int disp = BitConverter.ToInt32(image, i + 3);
				int count = CountTextSlots(image, pe, disp);
				if (count > bestCount)
				{
					bestCount = count;
					bestTable = disp;
				}
			}
			return bestCount >= MinTable ? bestTable : -1;
		}

		private static bool RbxIsImageBase(byte[] image, PeFile pe, PeFile.Section text, int movOff)
		{
			int from = Math.Max(text.Raw, movOff - 24);
			for (int i = movOff - 7; i >= from; i--)
			{
				// lea rbx, [rip+disp32]
				if (image[i] != 0x48 || image[i + 1] != 0x8D || image[i + 2] != 0x1D)
				{
					continue;
				}
				int disp = BitConverter.ToInt32(image, i + 3);
				int insnRva = text.Va + (i - text.Raw);
				long rbx = pe.ImageBase + insnRva + 7 + disp;
				return rbx == pe.ImageBase;
			}
			return false;
		}

		private static int CountTextSlots(byte[] image, PeFile pe, int tableRva)
		{
			int off = pe.RvaToOffset(tableRva);
			if (off < 0)
			{
				return 0;
			}
			int n = 0;
			while (n < 4096 && off + 4 <= image.Length)
			{
				int rel = BitConverter.ToInt32(image, off);
				if (!pe.InText(rel))
				{
					break;
				}
				n++;
				off += 4;
			}
			return n;
		}

		private static string Walk(byte[] image, PeFile pe, int rva, int pop, int depth, int limit)
		{
			int off = pe.RvaToOffset(rva);
			if (off < 0)
			{
				return "";
			}
			int avail = image.Length - off;
			if (avail > 0x800)
			{
				avail = 0x800;
			}
			if (avail < 1)
			{
				return "";
			}
			byte[] slice = new byte[avail];
			Buffer.BlockCopy(image, off, slice, 0, avail);
			Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(slice), (ulong)(pe.ImageBase + rva));
			List<string> events = new List<string>();
			int last = int.MinValue;
			int n = 0;
			bool followed = false;
			while (n < limit)
			{
				decoder.Decode(out Instruction insn);
				if (insn.IsInvalid)
				{
					break;
				}
				n++;
				if (insn.FlowControl == FlowControl.ConditionalBranch)
				{
					break;
				}
				if (insn.Mnemonic == Mnemonic.Mov && insn.Op0Register == Register.R8D && TryImm(insn, out int imm))
				{
					last = imm;
				}
				if (insn.Mnemonic == Mnemonic.Call && insn.Op0Kind == OpKind.NearBranch64)
				{
					int dst = (int)(insn.NearBranchTarget - (ulong)pe.ImageBase);
					if (dst == pop && last != int.MinValue)
					{
						events.Add("b" + last);
						last = int.MinValue;
					}
					else
					{
						string kind = Classify(image, pe, dst, pop);
						if (kind != null)
						{
							events.Add(kind);
						}
						else if (depth > 0 && !followed && n < 8)
						{
							string sub = Walk(image, pe, dst, pop, depth - 1, 160);
							if (!string.IsNullOrEmpty(sub))
							{
								events.Add(sub);
							}
							followed = true;
						}
					}
				}
				if (insn.Mnemonic == Mnemonic.Jmp)
				{
					if (insn.Op0Kind == OpKind.NearBranch64)
					{
						int dst = (int)(insn.NearBranchTarget - (ulong)pe.ImageBase);
						if (dst == pop && last != int.MinValue)
						{
							events.Add("b" + last);
							break;
						}
						if (depth > 0 && events.Count == 0 && !followed)
						{
							return Walk(image, pe, dst, pop, depth - 1, limit);
						}
					}
					break;
				}
				if (insn.Mnemonic == Mnemonic.Ret || insn.Mnemonic == Mnemonic.Int3)
				{
					break;
				}
			}
			return string.Join(" ", events);
		}

		private static string Classify(byte[] image, PeFile pe, int rva, int pop)
		{
			int off = pe.RvaToOffset(rva);
			if (off < 0)
			{
				return null;
			}
			int avail = Math.Min(0x60, image.Length - off);
			if (avail < 1)
			{
				return null;
			}
			byte[] slice = new byte[avail];
			Buffer.BlockCopy(image, off, slice, 0, avail);
			Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(slice), (ulong)(pe.ImageBase + rva));
			int last = int.MinValue;
			int pops = 0;
			int first = int.MinValue;
			bool ff35 = false;
			bool tailPop = false;
			bool ret = false;
			for (int n = 0; n < 16; n++)
			{
				decoder.Decode(out Instruction insn);
				if (insn.IsInvalid)
				{
					break;
				}
				if (insn.Mnemonic == Mnemonic.Mov && insn.Op0Register == Register.R8D && TryImm(insn, out int imm))
				{
					last = imm;
				}
				if (IsImm(insn, 0xFF35))
				{
					ff35 = true;
				}
				bool toPop = false;
				if ((insn.Mnemonic == Mnemonic.Call || insn.Mnemonic == Mnemonic.Jmp) && insn.Op0Kind == OpKind.NearBranch64)
				{
					int dst = (int)(insn.NearBranchTarget - (ulong)pe.ImageBase);
					toPop = dst == pop;
				}
				if (toPop && last != int.MinValue)
				{
					pops++;
					if (first == int.MinValue)
					{
						first = last;
					}
					if (insn.Mnemonic == Mnemonic.Jmp)
					{
						tailPop = true;
						break;
					}
					last = int.MinValue;
				}
				if (insn.Mnemonic == Mnemonic.Ret || insn.Mnemonic == Mnemonic.Int3 || insn.Mnemonic == Mnemonic.Jmp)
				{
					if (insn.Mnemonic == Mnemonic.Jmp && toPop)
					{
						tailPop = true;
					}
					ret = insn.Mnemonic == Mnemonic.Ret || tailPop;
					break;
				}
				if (insn.FlowControl == FlowControl.ConditionalBranch)
				{
					break;
				}
			}
			if (pops == 1 && first == 2 && ff35)
			{
				return "str16";
			}
			if (pops == 1 && (tailPop || ret))
			{
				return "b" + first;
			}
			return null;
		}

		private static bool TryImm(in Instruction insn, out int imm)
		{
			switch (insn.Op1Kind)
			{
			case OpKind.Immediate8:
				imm = insn.Immediate8;
				return true;
			case OpKind.Immediate8to32:
			case OpKind.Immediate8to64:
			case OpKind.Immediate32:
			case OpKind.Immediate32to64:
				imm = (int)insn.Immediate32;
				return true;
			default:
				imm = 0;
				return false;
			}
		}

		private static bool IsImm(in Instruction insn, int value)
		{
			if (insn.OpCount < 2)
			{
				return false;
			}
			int imm;
			if (!TryImm(insn, out imm))
			{
				return false;
			}
			return imm == value;
		}
	}
}
