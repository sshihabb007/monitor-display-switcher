using System;
using System.Collections.Generic;
using System.Globalization;

namespace DisplayInputSwitcher.Core
{
    /// <summary>
    /// Clean-room MCCS capabilities string parser matching VESA MCCS 2.x/3.0
    /// and ControlMyMonitor's vcp(...) discrete hex token extraction.
    /// </summary>
    public static class MccsCapabilitiesParser
    {
        public static MonitorCapabilities Parse(string rawMccs)
        {
            MonitorCapabilities caps = new MonitorCapabilities();
            if (string.IsNullOrWhiteSpace(rawMccs))
            {
                return caps;
            }

            caps.RawMccsString = rawMccs.Trim();
            caps.ModelFromCapabilities = ExtractTagContent(caps.RawMccsString, "model(");
            caps.MccsVersion = ExtractTagContent(caps.RawMccsString, "mccs_ver(");

            string vcpBody = ExtractTagContent(caps.RawMccsString, "vcp(");
            if (string.IsNullOrEmpty(vcpBody))
            {
                return caps;
            }

            int i = 0;
            int len = vcpBody.Length;
            while (i < len)
            {
                // Skip whitespace and stray commas
                while (i < len && (char.IsWhiteSpace(vcpBody[i]) || vcpBody[i] == ','))
                {
                    i++;
                }
                if (i >= len) break;

                // Read hex token for VCP code (1 or 2 hex chars)
                int codeStart = i;
                while (i < len && IsHexChar(vcpBody[i]))
                {
                    i++;
                }

                if (i > codeStart)
                {
                    string hexToken = vcpBody.Substring(codeStart, i - codeStart);
                    byte vcpCode;
                    if (hexToken.Length <= 2 && byte.TryParse(hexToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vcpCode))
                    {
                        caps.SupportedVcpCodes.Add(vcpCode);

                        // Skip optional spaces before '('
                        int lookahead = i;
                        while (lookahead < len && char.IsWhiteSpace(vcpBody[lookahead]))
                        {
                            lookahead++;
                        }

                        if (lookahead < len && vcpBody[lookahead] == '(')
                        {
                            i = lookahead + 1;
                            int depth = 1;
                            int listStart = i;
                            while (i < len && depth > 0)
                            {
                                if (vcpBody[i] == '(') depth++;
                                else if (vcpBody[i] == ')') depth--;
                                i++;
                            }
                            int listLen = (i - 1) - listStart;
                            if (listLen > 0)
                            {
                                string inner = vcpBody.Substring(listStart, listLen);
                                List<byte> discrete = ParseDiscreteHexBytes(inner);
                                if (discrete.Count > 0)
                                {
                                    caps.DiscreteValuesByVcp[vcpCode] = discrete;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Skip unknown parenthesis block or character safely
                    if (vcpBody[i] == '(')
                    {
                        int depth = 1;
                        i++;
                        while (i < len && depth > 0)
                        {
                            if (vcpBody[i] == '(') depth++;
                            else if (vcpBody[i] == ')') depth--;
                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }
            }

            return caps;
        }

        private static List<byte> ParseDiscreteHexBytes(string text)
        {
            List<byte> result = new List<byte>();
            int i = 0;
            int len = text.Length;
            while (i < len)
            {
                while (i < len && !IsHexChar(text[i]))
                {
                    i++;
                }
                if (i >= len) break;
                int start = i;
                while (i < len && IsHexChar(text[i]))
                {
                    i++;
                }
                string tok = text.Substring(start, i - start);
                byte val;
                if (tok.Length <= 2 && byte.TryParse(tok, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val))
                {
                    if (!result.Contains(val))
                    {
                        result.Add(val);
                    }
                }
            }
            return result;
        }

        private static string ExtractTagContent(string source, string tagOpen)
        {
            int idx = source.IndexOf(tagOpen, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;

            int contentStart = idx + tagOpen.Length;
            int depth = 1;
            int i = contentStart;
            while (i < source.Length && depth > 0)
            {
                char c = source[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                i++;
            }

            if (depth == 0 && i - 1 >= contentStart)
            {
                return source.Substring(contentStart, (i - 1) - contentStart).Trim();
            }
            return string.Empty;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F');
        }
    }
}

