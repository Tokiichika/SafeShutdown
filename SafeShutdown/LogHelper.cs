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

        public void Debug(string msg)
        {
            logger.Debug(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Debug(string msg, Exception err)
        {
            logger.Debug(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Info(string msg)
        {
            logger.Info(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Info(string msg, Exception err)
        {
            logger.Info(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }
        public void Warn(string msg)
        {
            logger.Warn(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Warn(string msg, Exception err)
        {
            logger.Warn(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Trace(string msg)
        {
            logger.Trace(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Trace(string msg, Exception err)
        {
            logger.Trace(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Error(string msg)
        {
            logger.Error(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Error(string msg, Exception err)
        {
            logger.Error(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Fatal(string msg)
        {
            logger.Fatal(msg);
            MainWindow.Instance().WriteLogBox(msg);
        }

        public void Fatal(string msg, Exception err)
        {
            logger.Fatal(err, msg);
            MainWindow.Instance().WriteLogBox(msg);
        }











    }
}
