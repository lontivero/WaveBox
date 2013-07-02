using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WaveBox.Service.Services;

namespace WaveBox.Service
{
	class ServiceFactory
	{
		/// <summary>
		/// Create a IService object which will be managed by ServiceManager
		/// <summary>
		public static IService CreateService(string service)
		{
			switch (service)
			{
				case "autoupdate":
					return new AutoUpdateService();
				case "cron":
					return new CronService();
				case "dynamicdns":
					return new DynamicDnsService();
				case "http":
					return new HttpService();
				case "nat":
					return new NatService();
				case "zeroconf":
					return new ZeroConfService();
				default:
					return null;
			}
		}
	}
}
