using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Threading;

namespace Collect
{
    //定义了一个名为 EcgEventHandler 的委托
    public delegate void EcgEventHandler(object sender, EcgEventArgs e);

    class SerialControl
    {
        //声明了一个名为 EcgEvent 的事件，该事件使用 EcgEventHandler 委托
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

        //解析每22个字节，checksum为前20个字节和，g_board_index为第3个字节数，g_packet_length为22数据包长度，g_data_length为第5到第18个字节
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
                    checksum += data; 
                    checksum2 += checksum;
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
                    g_data_length = 32;
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
        //发送数据
        public void comm_DataSend(byte[] Data)
        {
            comm.Write(Data, 0, Data.Length);
        }

        public bool Start(string com)
        {
            //如果线程已创建
            if (th != null)
            {
                g_err = "Thread which created for serial port has beed created.";
                LogHelper.WriteWarnLog("串口采集数据的线程已经创建");
                NlogHelper.WriteWarnLog("串口采集数据的线程已经创建");
                return false;
            }

            this.com = com;
            //初始化SerialPort对象  
            //设置或获取发送和接收数据时使用的行终止符
            comm.NewLine = "/r/n";
            comm.RtsEnable = false;//根据实际情况吧。  
            //添加事件注册  
            comm.DataReceived += comm_DataReceived;

            comm.PortName = com;
            comm.BaudRate = 115200;

            try
            {
                comm.Open();
                LogHelper.WriteInfoLog($"成功打开串口：{com}");
                NlogHelper.WriteInfoLog($"成功打开串口：{com}");
            }
            catch (Exception ex)
            {
                //现实异常信息给客户。  
                //MessageBox.Show(ex.Message);
                g_err = ex.Message;
                LogHelper.WriteErrorLog(ex.Message);
                NlogHelper.WriteErrorLog(ex.Message);
                return false;
            }

            if (comm.IsOpen == true)
            {
                run = true;
                th = new Thread(recvdata);
               
                th.Start();
                LogHelper.WriteInfoLog("串口采集数据线程成功创建,并开启");
                NlogHelper.WriteInfoLog("串口采集数据线程成功创建,并开启");
                g_bInstall = true;
                return true;
            }
            else
            {
                g_bInstall = false;
                LogHelper.WriteWarnLog("串口已经被关闭,无法开启");
                NlogHelper.WriteWarnLog("串口已经被关闭,无法开启");
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
                LogHelper.WriteInfoLog("串口采集线程已停止并且销毁");
                NlogHelper.WriteInfoLog("串口采集线程已停止并且销毁");
            }

            if (comm.IsOpen == true)
            {
                try
                {
                    comm.Close();
                    LogHelper.WriteInfoLog($"成功关闭串口：{com}");
                    NlogHelper.WriteInfoLog($"成功关闭串口：{com}");
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
                g_err = "Serial Port has been closed.";
                LogHelper.WriteWarnLog("串口已经被关闭");
                NlogHelper.WriteWarnLog("串口已经被关闭");
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
        public EcgEventArgs(string com, int type, byte[] value)
        {
            this.com = com;
            this.type = type;
            this.value = value;
        }

        public string com;
        public int type;
        public byte[] value;
    }
}
