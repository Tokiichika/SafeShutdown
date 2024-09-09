using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SafeShutdown
{
    public class LogHelper
    {
        private readonly NLog.Logger logger = LogManager.GetCurrentClassLogger();
        private static LogHelper obj;
        public static LogHelper WriteLog
        {
            get => obj ?? (new LogHelper());
            set => obj = value;
        }

        public void Info(string msg)
        {
            logger.Info("[Info]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Info(string msg, Exception err)
        {
            logger.Info(err, "[Info]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }
        public void Warn(string msg)
        {
            logger.Info("[Warn]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Warn(string msg, Exception err)
        {
            logger.Info(err, "[Warn]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Error(string msg)
        {
            logger.Info("[Error]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Error(string msg, Exception err)
        {
            logger.Info(err, "[Error]" + msg);
            MainWindow.Instance().WriteLogBox(msg);
        }
    }
}
