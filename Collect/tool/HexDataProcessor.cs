using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class HexDataProcessor
    {
        // 与TCPClient.cs中相同的变量
        private byte ifA0 = 0;
        private byte ifB0 = 0;
        private int g_packet_length = 0;
        private byte[] g_data = new byte[1024];

        // 存储完整的数据包
        private List<byte[]> completePackets = new List<byte[]>();

        // 处理单个字节数据的方法，与TCPClient.cs中的parse_data方法相同
        private int ParseData(byte data)
        {
            if (g_packet_length == 0)
            {
                if (data == 0xA0)
                {
                    g_data[g_packet_length] = data;
                    ifA0 = 1;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 1)
            {
                if (data == 0xB0)
                {
                    g_data[g_packet_length] = data;
                    ifB0 = 1;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length >= 2 && g_packet_length < 26)
            {
                g_data[g_packet_length] = data;
                g_packet_length++;
                return -1;
            }

            if (g_packet_length >= 26 && g_packet_length < 32)
            {
                if (data == 0x00)
                {
                    g_data[g_packet_length] = data;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 32)
            {
                if (data == 0xC0)
                {
                    g_data[g_packet_length] = data;

                    // 保存完整的数据包
                    byte[] completePacket = new byte[33];
                    Array.Copy(g_data, completePacket, 33);
                    completePackets.Add(completePacket);

                    // 重置状态
                    g_packet_length = 0;
                    ifA0 = 0;
                    ifB0 = 0;
                }
                else
                    g_packet_length = 0;
                return -1;
            }
            return -1;
        }

        // 将16进制字符串转换为字节数组
        private byte[] HexStringToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // 处理16进制文本文件
        public List<byte[]> ProcessHexFile(string filePath)
        {
            completePackets.Clear();
            g_packet_length = 0;
            ifA0 = 0;
            ifB0 = 0;
            try
            {
                string content = File.ReadAllText(filePath);
                string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 将每行的16进制字符串转换为字节数组
                    byte[] bytes = HexStringToBytes(line);

                    // 逐个字节处理
                    foreach (byte b in bytes)
                    {
                        ParseData(b);
                    }
                }

                Console.WriteLine($"成功处理文件: {filePath}");
                Console.WriteLine($"找到 {completePackets.Count} 个完整的数据包");

                return completePackets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件时出错: {ex.Message}");
                return new List<byte[]>();
            }
        }

        // 打印数据包内容
        public void PrintPackets()
        {
            for (int i = 0; i < completePackets.Count; i++)
            {
                Console.WriteLine($"数据包 {i + 1}:");
                byte[] packet = completePackets[i];

                // 打印为16进制格式
                string hexString = BitConverter.ToString(packet).Replace("-", " ");
                Console.WriteLine($"  16进制: {hexString}");

                // 打印前两个字节（A0 B0）
                Console.WriteLine($"  包头: 0x{packet[0]:X2} 0x{packet[1]:X2}");

                // 打印EEG数据部分（第3-26字节，共24字节，8个通道，每个通道3字节）
                Console.WriteLine("  EEG数据:");
                for (int j = 0; j < 8; j++)
                {
                    int startIndex = 2 + j * 3;
                    uint eegValue = (uint)((packet[startIndex] << 16) | (packet[startIndex + 1] << 8) | packet[startIndex + 2]);
                    Console.WriteLine($"    通道{j + 1}: 0x{eegValue:X6} ({eegValue})");
                }

                // 打印结尾（C0）
                Console.WriteLine($"  包尾: 0x{packet[32]:X2}");
                Console.WriteLine();
            }
        }

        // 获取EEG数据（提取第3-26字节）
        public List<byte[]> GetEEGData()
        {
            List<byte[]> eegDataList = new List<byte[]>();

            foreach (byte[] packet in completePackets)
            {
                byte[] eegData = new byte[24];
                Array.Copy(packet, 2, eegData, 0, 24);
                eegDataList.Add(eegData);
            }

            return eegDataList;
        }
    }
}
