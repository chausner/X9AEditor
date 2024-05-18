# X9A file format

This document describes the backup format (X9A files) that Yamaha CP88/CP73 instruments use to store settings and voices.

The overall file structure is based on the Yamaha YSFC file format as outlined in https://gist.github.com/chausner/158b70106369a72ef15ad4ec09e29d5e with the following differences:

* The version number in the header is 6.0.0.
* The only block types used are ELST, ESYS, DLST and DSYS.

ELST and ESYS blocks contain metadata and reference the corresponding data blocks DLST and DSYS which store the actual voice/system data.
There should be exactly one block of each type in the file and the order should be ELST, ESYS, DLST, DSYS.

## Encoding

Unless noted otherwise, all numbers are stored in big-endian format. Strings are stored in an encoding identical to ASCII except that the backslash character (\\) is replaced by the Yen sign (¥).

## ELST block payload

The ELST block stores metadata for each voice. There should always be exactly 20 * 8 entries, i.e. 8 voices per live set page.

| data type | field                       |
| --------- | --------------------------- |
| uint32    | number of following entries |

For each entry, the following structure follows:

| data type | field                        |
| --------- | ---------------------------- |
| char[4]   | "Entr" magic                 |
| uint32    | data size                    |
| uint32    | data offset                  |
| uint16    | unknown, always 0x003F       |
| uint8     | live set page (zero-based)   |
| uint8     | live set index (zero-based)  |
| char[]    | voice name (null-terminated) |

Data size and offset refer to the associated data of the entry in the DLST block.

## DLST block payload

| data type | field                                                                               |
| --------- | ----------------------------------------------------------------------------------- |
| uint32    | length of voice name, always 0x10                                                   |
| char[16]  | voice name (null-terminated, padded to fixed length with spaces or null characters) |

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, either 0x11 or 0x17                              |
| uint8     | unknown, always 0x00                                                  |
| uint8     | master switch                                                         |
| uint8     | advanced zone switch                                                  |
| uint8     | transpose (64: +0)                                                    |
| uint8     | split point (midi note number + 8, e.g. G2 = 0x37)                    |
| uint8     | FC1 assign                                                            |
| uint8     | FC2 assign                                                            |
| uint8     | delay/reverb section selection (0: all, 1: piano, 2: e.piano, 3: sub) |
| uint8     | modulation lever assign                                               |
| uint8     | modulation lever limit low                                            |
| uint8     | modulation lever limit high                                           |
| uint8     | FC1 assign (again)                                                    |
| uint8     | FC1 limit low                                                         |
| uint8     | FC1 limit high                                                        |
| uint8     | FC2 assign (again)                                                    |
| uint8     | FC2 limit low                                                         |
| uint8     | FC2 limit high                                                        |

Starting with firmware version 1.3, the following additional fields are present:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint8     | live set EQ mode switch                                               |
| uint8     | live set EQ on/off                                                    |
| uint8     | low gain (range: 52..76, 64: +0dB)                                    |
| uint8     | mid gain (range: 52..76, 64: +0dB)                                    |
| uint8     | mid gain frequency (range: 14..54)                                    |
| uint8     | high gain (range: 52..76, 64: +0dB)                                   |

Delay:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x7                                       |
| uint8     | delay on/off                                                          |
| uint8     | delay type (0: analog, 1: digital)                                    |
| uint8     | delay time                                                            |
| uint8     | delay feedback                                                        |
| uint8     | piano delay depth                                                     |
| uint8     | e.piano delay depth                                                   |
| uint8     | sub delay depth                                                       |

Reverb:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x5                                       |
| uint8     | reverb on/off                                                         |
| uint8     | reverb time                                                           |
| uint8     | piano reverb depth                                                    |
| uint8     | e.piano reverb depth                                                  |
| uint8     | sub reverb depth                                                      |

Master keyboard settings:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | number of zones, always 0x4                                           |

For each zone, the following structure follows:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x16                                      |
| uint8     | zone switch on/off                                                    |
| uint8     | Tx channel                                                            |
| uint8     | octave shift (64: 0)                                                  |
| uint8     | transpose (64: 0)                                                     |
| uint8     | note limit low (C-2: 0, G8: 127)                                      |
| uint8     | note limit high (C-2: 0, G8: 127)                                     |
| uint8     | Tx SW note                                                            |
| uint8     | Tx SW bank                                                            |
| uint8     | Tx SW program                                                         |
| uint8     | Tx SW volume                                                          |
| uint8     | Tx SW pan                                                             |
| uint8     | Tx SW pb                                                              |
| uint8     | Tx SW mod                                                             |
| uint8     | Tx SW fc1                                                             |
| uint8     | Tx SW fc2                                                             |
| uint8     | Tx SW fs                                                              |
| uint8     | Tx SW sustain                                                         |
| uint8     | bank msb                                                              |
| uint8     | bank lsb                                                              |
| uint8     | program change (zero-based)                                           |
| uint8     | volume                                                                |
| uint8     | pan (center: 64)                                                      |

Sections:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | number of sections, always 0x3                                        |

For each section, the following two structures follow:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, either 0x15, 0x17 or 0x1D                        |
| uint8     | voice category (zero-based)                                           |
| uint8     | voice number category 1 (zero-based)                                  |
| uint8     | voice number category 2 (zero-based)                                  |
| uint8     | voice number category 3 (zero-based)                                  |
| uint8     | voice number category 4 (zero-based)                                  |
| uint8     | voice advanced mode number (zero-based)                               |
| uint8     | on/off                                                                |
| uint8     | split (0: LR, 1: L, 2: R)                                             |
| uint8     | octave (64: +0)                                                       |
| uint8     | volume                                                                |
| uint8     | tone                                                                  |
| uint8     | pitch bend range (64: +0)                                             |
| uint8     | p.mod depth                                                           |
| uint8     | Rx switch expression                                                  |
| uint8     | Rx switch sustain                                                     |
| uint8     | Rx switch sostenuto                                                   |
| uint8     | Rx switch soft                                                        |
| uint8     | delay depth                                                           |
| uint8     | reverb depth                                                          |
| uint8     | advanced mode switch on/off                                           |
| uint8     | p.mod speed                                                           |

Starting with firmware version 1.5, the following additional fields are present:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint8     | touch sensitivity depth                                               |
| uint8     | touch sensitivity offset                                              |

Starting with firmware version 1.6, the following additional fields are present:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint8     | mono/poly (0: Mono, 1: Poly)                                           |
| uint8     | portamento switch                                                     |
| uint8     | portamento time                                                       |
| uint8     | portamento mode (0: Fingered, 1: Full-time)                           |
| uint8     | portamento time mode (0: Rate, 1: Time)                               |
| uint8     | pan (64: C)                                                           |

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x14                                      |
| uint8     | piano damper resonance                                                |
| uint8     | piano dsp on/off                                                      |
| uint8     | piano dsp category (0-3)                                              |
| uint8     | piano dsp depth                                                       |
| uint8     | e.piano dsp1 on/off                                                   |
| uint8     | e.piano dsp1 category (0-5)                                           |
| uint8     | e.piano dsp1 depth                                                    |
| uint8     | e.piano dsp1 rate                                                     |
| uint8     | e.piano dsp2 on/off                                                   |
| uint8     | e.piano dsp2 category (0-5)                                           |
| uint8     | e.piano dsp2 depth                                                    |
| uint8     | e.piano dsp2 speed                                                    |
| uint8     | e.piano drive on/off                                                  |
| uint8     | e.piano drive value                                                   |
| uint8     | sub dsp on/off                                                        |
| uint8     | sub dsp category (0-3)                                                |
| uint8     | sub dsp depth                                                         |
| uint8     | sub dsp speed                                                         |
| uint8     | sub dsp attack                                                        |
| uint8     | sub dsp release                                                       |

Live Set EQ:

Starting with firmware version 1.3, the following structure may be present:

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x4                                       |
| uint8     | low gain (range: 0..127, 64: +0dB)                                    |
| uint8     | mid gain (range: 0..127, 64: +0dB)                                    |
| uint8     | mid gain frequency (range: 0..127)                                    |
| uint8     | high gain (range: 0..127, 64: +0dB)                                   |

These values are similar to the live set EQ values mentioned above.
The only difference is that the values here use a larger numerical range.

## ESYS block payload

The ESYS block does not store any actual information and is simply a pointer to the actual system data in the DSYS block. 

| data type | field                                 |
| --------- | ------------------------------------- |
| uint32    | number of following entries, always 1 |

For the single entry, the following structure follows:

| data type | field                       |
| --------- | --------------------------- |
| char[4]   | "Entr" magic                |
| uint32    | data size                   |
| uint32    | data offset                 |
| uint16    | unknown, always 0x0000      |
| uint16    | unknown, always 0x0000      |
| char[7]   | "System", null-terminated   |

Data size and offset refer to the associated data of the entry in the DSYS block.

## DSYS block payload

| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x22                                      |
| uint8     | auto power off                                                        |
| uint8     | keyboard octave (64: +0)                                              |
| uint8     | transpose (64: +0)                                                    |
| uint8     | local control                                                         |
| uint8     | MIDI Tx channel (zero-based)                                          |
| uint8     | MIDI Rx channel (zero-based)                                          |
| uint8     | MIDI control (0: off, 1: on, 2: invert)                               |
| uint8     | unknown, always 0x00                                                  |
| uint8     | touch curve (0: normal, 4: fixed)                                     |
| uint8     | fixed velocity                                                        |
| uint8     | Tx/Rx bank select                                                     |
| uint8     | Tx/Rx prgm change                                                     |
| uint8     | MIDI port: MIDI in/out                                                |
| uint8     | MIDI port: USB in/out                                                 |
| uint8     | display lights: ins effect                                            |
| uint8     | display lights: section                                               |
| uint8     | display lights: LCD switch                                            |
| uint8     | value indication                                                      |
| uint8     | SW direction (normal: 0, reverse: 1)                                  |
| uint8     | LCD contrast                                                          |
| uint8     | panel lock settings: live set                                         |
| uint8     | panel lock settings: piano/e.piano/sub                                |
| uint8     | panel lock settings: delay/reverb                                     |
| uint8     | panel lock settings: master EQ                                        |
| uint8     | section hold                                                          |
| uint8     | live set view mode (close: 0, keep: 1)                                |
| uint8     | foot switch assign                                                    |
| uint8     | sustain pedal type (FC3A (HalfOn): 0, FC3A (HalfOff): 1, FC4A/FC5: 2) |
| uint8     | power on sound live set page (zero-based)                             |
| uint8     | power on sound live set index (zero-based)                            |
| uint8     | controller reset (hold: 0, reset: 1)                                  |
| uint8     | USB audio volume                                                      |
| uint8     | MIDI device number (zero-based, all: 0x10)                            |
| uint8     | MIDI control delay (* 100msec)                                        |
                                               
| data type | field                                                                 |
| --------- | --------------------------------------------------------------------- |
| uint32    | length of structure, always 0x4                                       |
| ushort*   | master tune (range: 0..0x7FF, 0: 414.72Hz, 0x7FF: 466.78Hz)           |
| byte      | unknown, always 0x5A                                                  |
| byte      | unknown, always 0x00                                                  |

\* encoded in little-endian
