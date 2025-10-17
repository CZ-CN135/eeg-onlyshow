using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect
{
    
    public class LogHelper
    {
        public static readonly log4net.ILog loginfo = log4net.LogManager.GetLogger("LogFileAppender");//这里的 loginfo 和 log4net.config 里的名字要一样
       
        public static void WriteInfoLog(string info)
        {
            if (loginfo.IsInfoEnabled)
            {
                loginfo.Info(info);
            }
            
        }

        public static void WriteErrorLog(string info)
        {
            if (loginfo.IsErrorEnabled)
            {
                loginfo.Error(info);
            }

        }
        public static void WriteWarnLog(string info)
        {
            if (loginfo.IsWarnEnabled)
            {
                loginfo.Warn(info);
            }

        }

    }
}
