# TRProtocol

TalesRunner Protocol Dump. Drop `trgame.exe` or `trgame_unpacked.exe` and it writes a CSV of opcode, name, and wire size.
<img width="560" height="400" alt="Screenshot 2026-10-15 233112" src="https://github.com/user-attachments/assets/676b2b56-a13d-4ed0-86f9-1baae692fa90" />

## trgame.exe and trgame_unpacked.exe

| File | Opcode and name | Wire |
| --- | --- | --- |
| `trgame.exe` | Yes | Skipped while `.text` is packed |
| `trgame_unpacked.exe` | Yes | Filled in |

Wire sizes come from the recv switch in `.text`. A packed client still dumps the name table. For the wire column, unpack the client first.

Unpack with [TRUnpacker](https://github.com/arstacth/TRUnpacker), then dump `trgame_unpacked.exe`.

## Use

Open `TRProtocol.exe`, browse to the client or drop it on the window, then click **Dump**. Choosing a file also starts the dump.

The CSV is saved next to the exe:

- `trgame.exe` → `trgame_opcode.csv`
- `trgame_unpacked.exe` → `trgame_unpacked_opcode.csv`

From a console:

```text
TRProtocol.exe trgame_unpacked.exe
```

With no arguments the window opens.

## CSV

```text
Opcode,Name,Wire
0,eServer_DUMMY,
1,eServer_LOGIN_REQ,b4
```

| Column | Meaning |
| --- | --- |
| Opcode | Index in the client name table |
| Name | Protocol name (`eServer_`, `eRoom_`, `eRelay`, `eUDP`, `eFarm`, `eMyRoom`) |
| Wire | Fixed prefix read before the first branch: `b1`, `b2`, `b4`, `b8`, or `str16`. Empty when that opcode has no straight prefix, or when wire was skipped |

A 32-bit client dumps names only. The status line says `wire skipped (32-bit client)`.

## Build

.NET 8 SDK, Windows x64. From the project folder:

```text
build.bat
```

That publishes one self-contained executable. Nothing else has to be copied with it, and .NET does not need to be installed to run it.

`bin\Release\net8.0-windows\win-x64\publish\TRProtocol.exe`
