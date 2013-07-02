﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Cirrious.MvvmCross.Plugins.Sqlite;
using Microsoft.Owin.Hosting;
using Mono.Unix.Native;
using Mono.Unix;
using Mono.Zeroconf;
using Ninject;
using WaveBox.Core.Injected;
using WaveBox.DeviceSync;
using WaveBox.Model;
using WaveBox.SessionManagement;
using WaveBox.Service;
using WaveBox.Static;
using WaveBox.TcpServer.Http;
using WaveBox.TcpServer;
using WaveBox.Transcoding;

namespace WaveBox
{
	class WaveBoxMain
	{
		// Logger
		private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		// HTTP server, which serves up the API
		private HttpServer httpServer;


		/// <summary>
		/// The main program for WaveBox.  Launches the HTTP server, initializes settings, creates default user,
		/// begins file scan, and then sleeps forever while other threads handle the work.
		/// </summary>
		public void Start()
		{
			if (logger.IsInfoEnabled) logger.Info("Initializing WaveBox " + WaveBoxService.BuildVersion + " on " + WaveBoxService.Platform + " platform...");

			// Initialize ImageMagick
			try
			{
				ImageMagickInterop.WandGenesis();
			}
			catch (Exception e)
			{
				logger.Error("Error loading ImageMagick DLL:", e);
			}

			// Create directory for WaveBox's root path, if it doesn't exist
			string rootDir = ServerUtility.RootPath();
			if (!Directory.Exists(rootDir))
			{
				Directory.CreateDirectory(rootDir);
			}

			// Perform initial setup of Settings, Database
			Injection.Kernel.Get<IDatabase>().DatabaseSetup();
			Injection.Kernel.Get<IServerSettings>().SettingsSetup();

			// Start services
			try
			{
				// Start default, required services
				ServiceManager.AddList(new List<string>{"cron", "http"});

				// Start additional services
				ServiceManager.AddList(Injection.Kernel.Get<IServerSettings>().Services);
				ServiceManager.StartAll();
			}
			catch (Exception e)
			{
				logger.Warn("Could not start WaveBox services, please check services in your configuration");
				logger.Warn(e);
			}

			// Start the HTTP server
			try
			{
				httpServer = new HttpServer(Injection.Kernel.Get<IServerSettings>().Port);
				StartTcpServer(httpServer);
			}
			catch (Exception e)
			{
				logger.Error("Could not start WaveBox HTTP server, please check port in your configuration");
				logger.Error(e);
				Environment.Exit(-1);
			}

			// Start the SignalR server for real time device state syncing
			try
			{
				WebApplication.Start<DeviceSyncStartup>("http://localhost:" + Injection.Kernel.Get<IServerSettings>().WsPort + "/");
			}
			catch (Exception e)
			{
				logger.Warn("Could not start WaveBox SignalR server, please check wsPort in your configuration");
				logger.Warn(e);
			}

			// Start ZeroConf
			// todo: allow ZeroConfService to get Server Url
			/*
			try
			{
				ZeroConf.PublishZeroConf(ServerUrl, Injection.Kernel.Get<IServerSettings>().Port);
			}
			catch (Exception e)
			{
				logger.Warn("Could not start WaveBox ZeroConf, please check port in your configuration");
				logger.Warn(e);
			}
			*/

			// Temporary: create test user
			new User.Factory().CreateUser("test", "test", null);

			// Start the UserManager
			UserManager.Setup();

			// Start file manager, calculate time it takes to run.
			if (logger.IsInfoEnabled) logger.Info("Scanning media directories...");
			FileManager.Setup();

			// Start podcast download queue
			PodcastManagement.DownloadQueue.FeedChecks.queueOperation(new FeedCheckOperation(0));
			PodcastManagement.DownloadQueue.FeedChecks.startQueue();

			// Start session scrub operation
			SessionScrub.Queue.queueOperation(new SessionScrubOperation(0));
			SessionScrub.Queue.startQueue();

			// Start checking for updates
			AutoUpdater.Start();

			return;
		}

		/// <summary>
		/// Initialize TCP server threads
		/// </summary>
		private void StartTcpServer(AbstractTcpServer server)
		{
			// Thread for server to run
			Thread t = null;

			// Attempt to start the server thread
			try
			{
				t = new Thread(new ThreadStart(server.Listen));
				t.IsBackground = true;
				t.Start();
			}
			// Catch any exceptions
			catch (Exception e)
			{
				// Print the message, quit.
				logger.Error(e);
				Environment.Exit(-1);
			}
		}

		/// <summary>
		/// Stop the WaveBox main
		/// </summary>
		public void Stop()
		{
			// Stop all running services
			ServiceManager.StopAll();
			ServiceManager.Clear();

			httpServer.Stop();

			// Dispose of ImageMagick
			try
			{
				ImageMagickInterop.WandTerminus();
			}
			catch (Exception e)
			{
				logger.Error("Error loading ImageMagick DLL:", e);
			}
		}

		/// <summary>
		/// Restart the WaveBox main
		/// </summary>
		public void Restart()
		{
			this.Stop();
			this.Start();
		}
	}
}
