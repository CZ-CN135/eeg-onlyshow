using Collect.Helper;
using NLog;
using NLog.Config;
using NLog.Targets.Wrappers;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace Collect
{
   
    public class NlogHelper
    {
        public static Logger logger = null;
       
        public static void WriteInfoLog(string info)
        {
            if (logger.IsInfoEnabled)
            {
                logger.Info(info);
            }

        }
        public static void WriteErrorLog(string info)
        {
            if (logger.IsErrorEnabled)
            {
                logger.Error(info);
            }

        }
        public static void WriteWarnLog(string info)
        {
            if (logger.IsWarnEnabled)
            {
                logger.Warn(info);
            }

        }
        public static void ConfigureNLogForRichTextBox()
        {
            var target = new WpfRichTextBoxTarget
            {
                Name = "RichText",
                Layout =
                    "${longdate:useUTC=false} - ${message} ${exception:innerFormat=tostring:maxInnerExceptionLevel=10:separator=,:format=tostring}",
                ControlName = "LogRichTextBox",
                FormName = "MainWindow1",
                AutoScroll = true,
                MaxLines = 100000,

                UseDefaultRowColoringRules = true,
            };
            var asyncWrapper = new AsyncTargetWrapper { Name = "RichTextAsync", WrappedTarget = target };
            var config = LogManager.Configuration ?? new LoggingConfiguration();

            config.AddTarget(asyncWrapper.Name, asyncWrapper);
            config.LoggingRules.Insert(0, new LoggingRule("*", LogLevel.Info, asyncWrapper));
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers(); // 强制重新加载配置
            if(logger==null)
            logger = LogManager.GetCurrentClassLogger();

        }
    }
}
