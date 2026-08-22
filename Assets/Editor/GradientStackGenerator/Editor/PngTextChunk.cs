// GradientStackGenerator - PngTextChunk.cs
// PNGファイルの tEXt チャンク（テクスチャファイル自体のメタデータ）に文字列を埋め込み/読み出しするユーティリティ。
// 生成テクスチャに Gradient プリセットのJSONを保存し、再読み込みして編集を再開するために使う。
using System.Collections.Generic;
using System.Text;

namespace GradientStackGenerator
{
    public static class PngTextChunk
    {
        static readonly byte[] kSig = { 137, 80, 78, 71, 13, 10, 26, 10 };

        static uint[] s_crcTable;
        static void EnsureCrcTable()
        {
            if (s_crcTable != null) return;
            s_crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                s_crcTable[n] = c;
            }
        }

        static uint Crc32(byte[] buf, int offset, int len)
        {
            EnsureCrcTable();
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < len; i++)
                c = s_crcTable[(c ^ buf[offset + i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        static void WriteBE(List<byte> dst, uint v)
        {
            dst.Add((byte)((v >> 24) & 0xFF));
            dst.Add((byte)((v >> 16) & 0xFF));
            dst.Add((byte)((v >> 8) & 0xFF));
            dst.Add((byte)(v & 0xFF));
        }

        static uint ReadBE(byte[] b, int pos)
        {
            return ((uint)b[pos] << 24) | ((uint)b[pos + 1] << 16) | ((uint)b[pos + 2] << 8) | b[pos + 3];
        }

        static bool ValidSig(byte[] png)
        {
            if (png == null || png.Length < 8) return false;
            for (int i = 0; i < 8; i++) if (png[i] != kSig[i]) return false;
            return true;
        }

        /// <summary>PNGバイト列に tEXt チャンク(keyword+text)を追加して返す。既存の同キーワードは除去して置き換える。</summary>
        public static byte[] Inject(byte[] png, string keyword, string text)
        {
            if (!ValidSig(png)) return png;

            // まず同キーワードの既存tEXtを取り除く
            png = RemoveText(png, keyword);

            // IEND チャンクの位置を探す
            int iendPos = FindChunk(png, "IEND");
            if (iendPos < 0) return png;

            // tEXt データ = keyword + 0x00 + text（UTF-8で自己完結）
            byte[] kw = Encoding.ASCII.GetBytes(keyword);
            byte[] tx = Encoding.UTF8.GetBytes(text);
            var data = new List<byte>(kw.Length + 1 + tx.Length);
            data.AddRange(kw);
            data.Add(0);
            data.AddRange(tx);
            byte[] dataArr = data.ToArray();

            // チャンク = length + "tEXt" + data + crc(type+data)
            var chunk = new List<byte>();
            WriteBE(chunk, (uint)dataArr.Length);
            byte[] typeArr = Encoding.ASCII.GetBytes("tEXt");
            chunk.AddRange(typeArr);
            chunk.AddRange(dataArr);
            var crcBuf = new byte[typeArr.Length + dataArr.Length];
            System.Array.Copy(typeArr, 0, crcBuf, 0, typeArr.Length);
            System.Array.Copy(dataArr, 0, crcBuf, typeArr.Length, dataArr.Length);
            WriteBE(chunk, Crc32(crcBuf, 0, crcBuf.Length));

            // IEND の直前に挿入
            var result = new byte[png.Length + chunk.Count];
            System.Array.Copy(png, 0, result, 0, iendPos);
            chunk.CopyTo(result, iendPos);
            System.Array.Copy(png, iendPos, result, iendPos + chunk.Count, png.Length - iendPos);
            return result;
        }

        /// <summary>指定キーワードの tEXt テキストを取り出す。無ければ null。</summary>
        public static string Extract(byte[] png, string keyword)
        {
            if (!ValidSig(png)) return null;
            int pos = 8;
            while (pos + 12 <= png.Length)
            {
                uint clen = ReadBE(png, pos);
                string ctype = Encoding.ASCII.GetString(png, pos + 4, 4);
                int dataStart = pos + 8;
                if (ctype == "tEXt" && dataStart + (int)clen <= png.Length)
                {
                    int end = dataStart + (int)clen;
                    int nul = -1;
                    for (int i = dataStart; i < end; i++) { if (png[i] == 0) { nul = i; break; } }
                    if (nul >= 0)
                    {
                        string kw = Encoding.ASCII.GetString(png, dataStart, nul - dataStart);
                        if (kw == keyword)
                            return Encoding.UTF8.GetString(png, nul + 1, end - (nul + 1));
                    }
                }
                if (ctype == "IEND") break;
                pos = dataStart + (int)clen + 4; // +crc
            }
            return null;
        }

        static byte[] RemoveText(byte[] png, string keyword)
        {
            int pos = 8;
            while (pos + 12 <= png.Length)
            {
                uint clen = ReadBE(png, pos);
                string ctype = Encoding.ASCII.GetString(png, pos + 4, 4);
                int dataStart = pos + 8;
                int chunkTotal = 12 + (int)clen;
                if (ctype == "tEXt" && dataStart + (int)clen <= png.Length)
                {
                    int end = dataStart + (int)clen;
                    int nul = -1;
                    for (int i = dataStart; i < end; i++) { if (png[i] == 0) { nul = i; break; } }
                    if (nul >= 0)
                    {
                        string kw = Encoding.ASCII.GetString(png, dataStart, nul - dataStart);
                        if (kw == keyword)
                        {
                            var res = new byte[png.Length - chunkTotal];
                            System.Array.Copy(png, 0, res, 0, pos);
                            System.Array.Copy(png, pos + chunkTotal, res, pos, png.Length - (pos + chunkTotal));
                            return res;
                        }
                    }
                }
                if (ctype == "IEND") break;
                pos += chunkTotal;
            }
            return png;
        }

        static int FindChunk(byte[] png, string type)
        {
            int pos = 8;
            while (pos + 12 <= png.Length)
            {
                uint clen = ReadBE(png, pos);
                string ctype = Encoding.ASCII.GetString(png, pos + 4, 4);
                if (ctype == type) return pos;
                pos += 12 + (int)clen;
            }
            return -1;
        }
    }
}
