using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.RightsManagement;
using System.Threading;
using System.Windows;

namespace Collect.EEG
{
    //定义了一个名为 EcgEventHandler 的委托
    public delegate void EcgTCPEventHandler(object sender, EcgTCPEventArgs e);
    public class TCPClient
    {
        //声明了一个名为 EcgEvent 的事件，该事件使用 EcgEventHandler 委托
        public event EcgTCPEventHandler EcgEvent;

        TcpClient client;
        public TcpListener tcpListener;
        string IPAdress;
        int Port;
        public string freq2;
        public string duty2;
        public string time2;


        Thread th = null;
        bool run = false;

        public bool g_bInstall = false;

        string g_err = "Init";

        byte g_data_length_num = 3;
        byte ifA0 = 0;
        byte ifB0 = 0;
        byte ifC0 = 0;
        int g_packet_length = 0;
        int g_data_length = 0;
        byte g_board_index = 0;
        byte checksum = 0;
        byte checksum2 = 0;
        byte[] g_data = new byte[1024];
        //int parse_data(byte data)
        //{
        //    if (g_packet_length == 0)
        //    {
        //        if (data == 0xAA)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum = data; checksum2 = checksum;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length == 1)
        //    {
        //        if (data == 0xFF)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum += data;
        //            checksum2 += checksum;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length == 2)
        //    {
        //        g_board_index = data;
        //        g_data[g_packet_length] = data;
        //        checksum += data; checksum2 += checksum;
        //        g_packet_length++;
        //        return -1;
        //    }

        //    if (g_packet_length == 3)
        //    {
        //        if (data == 0x10)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum += data; checksum2 += checksum;
        //            g_data_length = 16;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length < g_data_length + 4)
        //    {
        //        g_data[g_packet_length] = data;
        //        checksum += data; checksum2 += checksum;
        //        g_packet_length++;
        //        return -1;
        //    }

        //    if (g_packet_length == g_data_length + 4)
        //    {
        //        if (checksum == data)
        //        {
        //            g_data[g_packet_length] = data;
        //            g_packet_length++;
        //        }
        //        else
        //        {
        //            g_packet_length = 0;
        //            checksum = 0;
        //            checksum2 = 0;
        //        }
        //        return -1;
        //    }

        //    if (g_packet_length == g_data_length + 5)
        //    {
        //        if (checksum2 == data)
        //        {
        //            g_data[g_packet_length] = data;
        //            if (EcgEvent != null)
        //                EcgEvent(this, new EcgTCPEventArgs(g_data));
        //        }
        //        g_packet_length = 0;
        //        checksum = 0;
        //        checksum2 = 0;
        //    }

        //    return -1;
        //}
        int parse_data(byte data)
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
                    if (EcgEvent != null)
                        EcgEvent(this, new EcgTCPEventArgs(g_data));
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
        //解析每22个字节，checksum为前20个字节和，g_board_index为第3个字节数，g_packet_length为22数据包长度，g_data_length为第5到第18个字节
        //int parse_data(byte data)
        //{
        //    if (g_packet_length == 0)
        //    {
        //        if (data == 0xAA)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum = data; checksum2 = checksum;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length == 1)
        //    {
        //        if (data == 0xFF)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum += data;
        //            checksum2 += checksum;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length == 2)
        //    {
        //        g_board_index = data;
        //        g_data[g_packet_length] = data;
        //        checksum += data; checksum2 += checksum;
        //        g_packet_length++;
        //        return -1;
        //    }

        //    if (g_packet_length == 3)
        //    {
        //        if (data == 0x10)
        //        {
        //            g_data[g_packet_length] = data;
        //            checksum += data; checksum2 += checksum;
        //            g_data_length = 16;
        //            g_packet_length++;
        //        }
        //        else
        //            g_packet_length = 0;
        //        return -1;
        //    }

        //    if (g_packet_length < g_data_length + 4)
        //    {
        //        g_data[g_packet_length] = data;
        //        checksum += data; checksum2 += checksum;
        //        g_packet_length++;
        //        return -1;
        //    }

        //    if (g_packet_length == g_data_length + 4)
        //    {
        //        if (checksum == data)
        //        {
        //            g_data[g_packet_length] = data;
        //            g_packet_length++;
        //        }
        //        else
        //        {
        //            g_packet_length = 0;
        //            checksum = 0;
        //            checksum2 = 0;
        //        }
        //        return -1;
        //    }

        //    if (g_packet_length == g_data_length + 5)
        //    {
        //        if (checksum2 == data)
        //        {
        //            g_data[g_packet_length] = data;
        //            if (EcgEvent != null)
        //                EcgEvent(this, new EcgTCPEventArgs(g_data));
        //        }
        //        g_packet_length = 0;
        //        checksum = 0;
        //        checksum2 = 0;
        //    }

        //    return -1;
        //}

        Queue<byte> queue = new Queue<byte>();
        public bool IsWri = false;
        public bool IsWri_start = false;
        public bool IsWri_stop = false;
        public void Senddata(NetworkStream networkStream,string freq,string duty,string time)
        {
            string binaryString = Convert.ToString(int.Parse(time), 2).PadLeft(16, '0');
            string highBits = binaryString.Substring(0, 8);
            string lowBits = binaryString.Substring(8, 8);
            var timeH = Convert.ToByte(highBits, 2);
            var timeL = Convert.ToByte(lowBits, 2);

            var freq1=Byte.Parse(freq);
            var duty1=Byte.Parse(duty);
    
            byte[] sendBuffer = new byte[5] { 255, freq1, duty1,timeH,timeL };
            networkStream.Write(sendBuffer, 0, sendBuffer.Length);
            IsWri = false;
        }
        void recvdata()
        {
            using (var stream = client.GetStream())
            {

                while (run)
                {
                    try
                    {
                        if (IsWri)
                        {
                            Senddata(stream, freq2, duty2, time2);
                            IsWri=false;
                        }
                        if (IsWri_start)
                        {
                            byte[] sendBuffer = new byte[3] { 255, 241, 254 };
                            stream.Write(sendBuffer, 0, sendBuffer.Length);
                            NlogHelper.WriteInfoLog("已发送开始采集指令");
                            IsWri_start = false;
                        }
                        if (IsWri_stop)
                        {
                       
                            byte[] sendBuffer = new byte[3] { 255, 242, 254 };
                            stream.Write(sendBuffer, 0, sendBuffer.Length);
                            NlogHelper.WriteInfoLog("已发送停止采集指令");
                            
                            
                            IsWri_stop=false;
                        }
                        if (client.Available > 0)
                        {
                            byte[] buffer = new byte[client.Available];
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            
                            if (bytesRead == 0)
                            {
                                NlogHelper.WriteInfoLog("未收到数据");
                                break;
                            }
                            Monitor.Enter(queue);
                            for (int i = 0; i < bytesRead; i++)
                                queue.Enqueue(buffer[i]);
                            Monitor.Exit(queue);
                        }
                        if (queue.Count >= 1)
                        {
                            Monitor.Enter(queue);
                            byte tempdata = queue.Dequeue();
                            Monitor.Exit(queue);
                            parse_data(tempdata);
                        }
                    }
                    catch (Exception ex)
                    {
                
                        LogHelper.WriteErrorLog(ex.Message);
                        NlogHelper.WriteErrorLog(ex.Message);
                        break;
                    }

                }
            }

        }
      



        public  bool Start(string ip, int port)
        {
            //如果线程已创建
            if (th != null)
            {
                g_err = "Thread which created for TCP port has beed created.";
                LogHelper.WriteInfoLog("TCP采集数据的线程已经创建");
                NlogHelper.WriteInfoLog("TCP采集数据的线程已经创建");
                return false;
            }

            this.IPAdress = ip;
            this.Port = port;

            try
            {
                client = new TcpClient();
                client.Connect(IPAddress.Parse(IPAdress), Port);
                LogHelper.WriteInfoLog("TCP与单片机连接成功");
                NlogHelper.WriteInfoLog("TCP与单片机连接成功");
            }
            catch (Exception ex)
            {
               
                g_err = ex.Message;
                LogHelper.WriteErrorLog(ex.Message);
                NlogHelper.WriteErrorLog(ex.Message);
                return false;
            }

            if (client.Connected == true)
            {
                run = true;
                th = new Thread(recvdata);
                th.Start();
                LogHelper.WriteInfoLog("TCP采集数据线程成功创建,并开启");
                NlogHelper.WriteInfoLog("TCP采集数据线程成功创建,并开启");
                g_bInstall = true;
                return true;
            }
            else
            {
                g_bInstall = false;
                LogHelper.WriteErrorLog("TCP与单片机断开连接");
                NlogHelper.WriteErrorLog("TCP与单片机断开连接");
                return false;
            }
        }

        public string GetLastError()
        {
            return g_err;
        }

        public bool Stop()
        {
            run = false;
            IsWri_stop = true;

            if (th != null)
            {
                th.Join();
                th = null;
                LogHelper.WriteInfoLog("TCP采集线程已停止并且销毁");
                NlogHelper.WriteInfoLog("TCP采集线程已停止并且销毁");

            }

            if (client.Connected == true)
            {
                try
                {
                    client.Close();
                    LogHelper.WriteInfoLog("TCP成功与单片机断开连接");
                    NlogHelper.WriteInfoLog("TCP成功与单片机断开连接");
                }
                catch (Exception ex)
                {
                    g_err = ex.Message;
                    LogHelper.WriteErrorLog(ex.Message);
                    NlogHelper.WriteErrorLog(ex.Message);
                    return false;
                }
            }
            else
            {
                g_err = "TCP Port has been closed.";
                LogHelper.WriteWarnLog("TCPd断开已经被关闭");
                NlogHelper.WriteWarnLog("TCPd断开已经被关闭");
                return false;
            }

            g_bInstall = false;
            return true;
        }
    }
    public class EcgTCPEventArgs : EventArgs
    {
        public string com;
        public int type;
        public byte[] value;
        //命令与值
        //1、表示原始数据 0x80
        //2、代表算好的心率 0x03
        //3、表示信号质量 0x02
        public EcgTCPEventArgs(byte[] value)
        {

            this.value = value;
        }


    }
}
