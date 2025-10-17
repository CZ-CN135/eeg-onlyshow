using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Threading;
using Collect.Power;

namespace Collect
{
    public delegate void EcgEventHandler(object sender, EcgEventArgs e);

    class SerialControl
    {
        public event EcgEventHandler EcgEvent;

        string com;
        SerialPort comm = new SerialPort();

        Thread th = null;
        bool run = false;

        public bool g_bInstall = false;

        string g_err="Init";

        int g_packet_length = 0;
        byte g_data_length = 0;
        byte g_board_index = 0;
        byte checksum = 0;
        byte checksum2 = 0;
        byte[] g_data = new byte[256];
        int parse_data(byte data)
        {
            if (g_packet_length == 0)
            {
                if (data == 0xAA)
                {
                    g_data[g_packet_length] = data;
                    checksum = data; checksum2 = checksum;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 1)
            {
                if (data == 0xFF)
                {
                    g_data[g_packet_length] = data;
                    checksum += data; checksum2 += checksum;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 2)
            {
                g_board_index = data;
                g_data[g_packet_length] = data;
                checksum += data; checksum2 += checksum;
                g_packet_length++;
                return -1;
            }

            if (g_packet_length == 3)
            {
                if (data == 0x10)
                {
                    g_data[g_packet_length] = data;
                    checksum += data; checksum2 += checksum;
                    g_data_length = 16;
                    g_packet_length++;
                }
                else
                    g_packet_length = 0;
                return -1;
            }

            if (g_packet_length < g_data_length+4)
            {
                g_data[g_packet_length] = data;
                checksum += data; checksum2 += checksum;
                g_packet_length++;
                return -1;
            }
            
            if (g_packet_length == g_data_length + 4)
            {
                if (checksum == data)
                {
                    g_data[g_packet_length] = data;
                    g_packet_length++;
                }
                else
                {
                    g_packet_length = 0;
                    checksum = 0;
                    checksum2 = 0;
                }
                return -1;
            }

            if (g_packet_length == g_data_length + 5)
            {
                if (checksum2 == data)
                {
                    g_data[g_packet_length] = data;
                    if (EcgEvent != null)
                        EcgEvent(this, new EcgEventArgs(com, g_board_index, g_data));
                }
                g_packet_length = 0;
                checksum = 0;
                checksum2 = 0;
            }

            return -1;
        }

        void recvdata()
        {
            while (run)
            {
                if (queue.Count >= 1)
                {
                    Monitor.Enter(queue);
                    byte tempdata = queue.Dequeue();
                    Monitor.Exit(queue);

                    parse_data(tempdata);

                    //send message here
                    //if (EcgEvent != null)
                    //    EcgEvent(this, new EcgEventArgs(com, 1, 100));
                }
            }
        }

        Queue<byte> queue = new Queue<byte>();
        void comm_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int n = comm.BytesToRead;//先记录下来，避免某种原因，人为的原因，操作几次之间时间长，缓存不一致  
            byte[] buf = new byte[n];//声明一个临时数组存储当前来的串口数据  
            //received_count += n;//增加接收计数  
            comm.Read(buf, 0, n);//读取缓冲数据  

            Monitor.Enter(queue);
            for (int i = 0; i < n; i++)
             queue.Enqueue(buf[i]);
            Monitor.Exit(queue);
        }
        public void comm_DataSend(byte[] Data)
        {
            comm.Write(Data, 0, Data.Length);
        }

        public bool Start(string com)
        {
            if (th != null)
            {
                g_err = "Thread which created for serial port has beed created.";
                return false;
            }

            this.com = com;
            //初始化SerialPort对象  
            comm.NewLine = "/r/n";
            comm.RtsEnable = false;//根据实际情况吧。  
            //添加事件注册  
            comm.DataReceived += comm_DataReceived;

            comm.PortName = com;
            comm.BaudRate = 115200;

            try
            {
                comm.Open();
            }
            catch (Exception ex)
            {
                //现实异常信息给客户。  
                //MessageBox.Show(ex.Message);
                g_err = ex.Message;
                return false;
            }

            if (comm.IsOpen == true)
            {
                run = true;
                th = new Thread(recvdata);
                th.Start();

                g_bInstall = true;
                return true;
            }
            else
            {
                g_bInstall = false;
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

            if (th != null)
            {
                th.Join();
                th = null;
            }

            if (comm.IsOpen == true)
            {
                try
                {
                    comm.Close();
                }
                catch (Exception ex)
                {
                    g_err = ex.Message;
                    return false;
                }
            }
            else
            {
                g_err = "Serial Port has been closed.";
                return false;
            }

            g_bInstall = false;
            return true;
        }
    }

    //用于设备的命令解析消息
    public class EcgEventArgs : EventArgs
    {
        //命令与值
        //1、表示原始数据 0x80
        //2、代表算好的心率 0x03
        //3、表示信号质量 0x02
        //4、表示频谱分析结果 "spectrum"
        public EcgEventArgs(string com, int type, byte[] value)
        {
            this.com = com;
            this.type = type;
            this.value = value;
            this.psdResult = null;
        }

        // 新增构造函数，用于传递频谱分析结果
        public EcgEventArgs(string com, int channelIndex, PowerSpectralDensityAnalyzer.PSDResult psdResult)
        {
            this.com = com;
            this.type = channelIndex;
            this.value = null;
            this.psdResult = psdResult;
        }

        public string com;
        public int type;
        public byte[] value;
        public PowerSpectralDensityAnalyzer.PSDResult psdResult; // 频谱分析结果
    }
}
