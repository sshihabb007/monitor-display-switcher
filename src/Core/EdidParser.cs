using System;
using System.Text;

namespace DisplayInputSwitcher.Core
{
    public static class EdidParser
    {
        private static readonly byte[] EdidHeader = new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

        public static EdidMetadata Parse(byte[] edid)
        {
            EdidMetadata meta = new EdidMetadata();
            if (edid == null || edid.Length < 128)
            {
                return meta;
            }

            for (int i = 0; i < 8; i++)
            {
                if (edid[i] != EdidHeader[i])
                {
                    return meta;
                }
            }

            meta.IsValid = true;

            // PnP 3-letter manufacturer ID at bytes 8..9 (big-endian compressed 5-bit ASCII)
            int mfgWord = (edid[8] << 8) | edid[9];
            int c1 = (mfgWord >> 10) & 0x1F;
            int c2 = (mfgWord >> 5) & 0x1F;
            int c3 = mfgWord & 0x1F;
            if (c1 >= 1 && c1 <= 26 && c2 >= 1 && c2 <= 26 && c3 >= 1 && c3 <= 26)
            {
                meta.ManufacturerCode = new string(new char[]
                {
                    (char)('A' + c1 - 1),
                    (char)('A' + c2 - 1),
                    (char)('A' + c3 - 1)
                });
            }

            meta.ProductCode = (ushort)(edid[10] | (edid[11] << 8));

            uint headerSerial = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));

            // Four 18-byte descriptor blocks at offsets 54, 72, 90, 108
            int[] offsets = new int[] { 54, 72, 90, 108 };
            for (int idx = 0; idx < offsets.Length; idx++)
            {
                int off = offsets[idx];
                if (off + 18 > edid.Length) break;

                // Monitor descriptor starts with 00 00 00 <tag> 00
                if (edid[off] == 0x00 && edid[off + 1] == 0x00 && edid[off + 2] == 0x00)
                {
                    byte tag = edid[off + 3];
                    if (tag == 0xFC)
                    {
                        string name = ExtractDescriptorText(edid, off + 5, 13);
                        if (!string.IsNullOrEmpty(name))
                        {
                            meta.MonitorName = name;
                        }
                    }
                    else if (tag == 0xFF)
                    {
                        string serial = ExtractDescriptorText(edid, off + 5, 13);
                        if (!string.IsNullOrEmpty(serial))
                        {
                            meta.SerialNumber = serial;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(meta.SerialNumber) && headerSerial != 0 && headerSerial != 0xFFFFFFFFu && headerSerial != 0x01010101u)
            {
                meta.SerialNumber = headerSerial.ToString();
            }

            return meta;
        }

        private static string ExtractDescriptorText(byte[] edid, int start, int length)
        {
            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length && (start + i) < edid.Length; i++)
            {
                byte b = edid[start + i];
                if (b == 0x00 || b == 0x0A || b == 0x0D)
                {
                    break;
                }
                if (b >= 0x20 && b <= 0x7E)
                {
                    sb.Append((char)b);
                }
            }
            return sb.ToString().Trim();
        }
    }
}

