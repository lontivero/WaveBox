using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Ninject;
using WaveBox.Core;
using WaveBox.Core.ApiResponse;
using WaveBox.Core.Extensions;
using WaveBox.Core.Model.Repository;
using WaveBox.Core.Model;
using WaveBox.Service.Services.Http;
using WaveBox.Service.Services;
using WaveBox.Service;
using WaveBox.Static;

namespace WaveBox.ApiHandler.Handlers
{
	class JukeboxApiHandler : IApiHandler
	{
		private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public string Name { get { return "jukebox"; } }

		// Allow standard users to control jukebox
		public bool CheckPermission(User user, string action)
		{
			return user.HasPermission(Role.User);
		}

		/// <summary>
		/// Process returns whether a specific call the Jukebox API was successful or not
		/// </summary>
		public void Process(UriWrapper uri, IHttpProcessor processor, User user)
		{
			// Fetch JukeboxService instance
			JukeboxService jukebox = (JukeboxService)ServiceManager.GetInstance("jukebox");

			// Ensure Jukebox service is ready
			if ((object)jukebox == null)
			{
				processor.WriteJson(new JukeboxResponse("JukeboxService is not running!", null, null));
				return;
			}

			int index = 0;
			string s = "";

			// Jukebox calls must contain an action
			if (uri.Parameters.ContainsKey("action"))
			{
				string action = null;
				uri.Parameters.TryGetValue("action", out action);

				// Look for valid actions within the parameters
				if (new string[] {"play", "pause", "stop", "prev", "next", "status", "playlist", "add", "remove", "move", "clear"}.Contains(action))
				{
					switch (action)
					{
						case "play":
							string indexString = null;
							if (uri.Parameters.ContainsKey("index"))
							{
								uri.Parameters.TryGetValue("index", out indexString);
								Int32.TryParse(indexString, out index);
							}

							if (indexString == null)
							{
								jukebox.Play();
							}
							else
							{
								jukebox.PlaySongAtIndex(index);
							}

							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "pause":
							jukebox.Pause();
							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "stop":
							jukebox.StopPlayback();
							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "prev":
							jukebox.Prev();
							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "next":
							jukebox.Next();
							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "status":
							processor.WriteJson(new JukeboxResponse(null, CreateJukeboxStatus(), null));
							break;
						case "playlist":
							processor.WriteJson(new JukeboxResponse(null, null, jukebox.ListOfSongs()));
							break;
						case "add":
							if (uri.Parameters.ContainsKey("id"))
							{
								s = "";
								uri.Parameters.TryGetValue("id", out s);
								if (this.AddSongs(s, processor))
								{
									processor.WriteJson(new JukeboxResponse(null, null, jukebox.ListOfSongs()));
								}
							}
							break;
						case "remove":
							if (uri.Parameters.ContainsKey("index"))
							{
								s = "";
								uri.Parameters.TryGetValue("index", out s);
								RemoveSongs(s);
								processor.WriteJson(new JukeboxResponse(null, null, jukebox.ListOfSongs()));
							}
							break;
						case "move":
							if (uri.Parameters.ContainsKey("index"))
							{
								string[] arr = null;
								if (uri.Parameters.TryGetValue("index", out s))
								{
									arr = s.Split(',');
									if (arr.Length == 2)
									{
										if (Move(arr[0], arr[1]))
										{
											processor.WriteJson(new JukeboxResponse(null, null, jukebox.ListOfSongs()));
										}
									}
									else
									{
										logger.IfInfo("Move: Invalid number of indices");
										processor.WriteJson(new JukeboxResponse("Invalid number of indices for action 'move'", null, null));
									}
								}
							}
							else
							{
								logger.IfInfo("Move: Missing 'index' parameter");
								processor.WriteJson(new JukeboxResponse("Missing 'index' parameter for action 'move'", null, null));
							}
							break;
						case "clear":
							jukebox.ClearPlaylist();
							processor.WriteJson(new JukeboxResponse(null, null, null));
							break;
						default:
							// This should never happen, unless we forget to add a case.
							processor.WriteJson(new JukeboxResponse("You broke WaveBox", null, null));
							break;
					}
				}
				else
				{
					// Else, invalid action specified, return an error
					processor.WriteJson(new JukeboxResponse("Invalid action '" + action + "' specified", null, null));
				}
			}
			else
			{
				// Else, no action provided, return an error
				processor.WriteJson(new JukeboxResponse("No action specified for jukebox", null, null));
			}
		}

		/// <summary>
		/// Add comma-separated list of songs to the Jukebox
		/// </summary>
		private bool AddSongs(string songIds, IHttpProcessor processor)
		{
			bool allSongsAddedSuccessfully = true;
			IList<Song> songs = new List<Song>();
			foreach (string p in songIds.Split(','))
			{
				try
				{
					if (Injection.Kernel.Get<IItemRepository>().ItemTypeForItemId(int.Parse(p)) == ItemType.Song)
					{
						Song s = Injection.Kernel.Get<ISongRepository>().SongForId(int.Parse(p));
						songs.Add(s);
					}
					else
					{
						allSongsAddedSuccessfully = false;
					}
				}
				catch (Exception e)
				{
					logger.Error("Error getting songs to add: ", e);
					processor.WriteJson(new JukeboxResponse("Error getting songs to add", null, null));
					return false;
				}
			}

			// Fetch JukeboxService instance
			JukeboxService jukebox = (JukeboxService)ServiceManager.GetInstance("jukebox");

			jukebox.AddSongs(songs);

			if (!allSongsAddedSuccessfully)
			{
				processor.WriteJson(new JukeboxResponse("One or more items provided were not of the appropriate type and were not added to the playlist.", null, jukebox.ListOfSongs()));
			}

			return allSongsAddedSuccessfully;
		}

		/// <summary>
		/// Remove comma-separated list of songs from Jukebox
		/// </summary>
		private void RemoveSongs(string songIds)
		{
			IList<int> indices = new List<int>();
			foreach (string p in songIds.Split(','))
			{
				try
				{
					indices.Add(int.Parse(p));
				}
				catch (Exception e)
				{
					logger.Error("Error getting songs to remove: ", e);
				}
			}

			// Fetch JukeboxService instance
			JukeboxService jukebox = (JukeboxService)ServiceManager.GetInstance("jukebox");

			jukebox.RemoveSongsAtIndexes(indices);
		}

		/// <summary>
		/// Move song in Jukebox
		/// </summary>
		private bool Move(string from, string to)
		{
			try
			{
				int fromI = int.Parse(from);
				int toI = int.Parse(to);

				// Fetch JukeboxService instance
				JukeboxService jukebox = (JukeboxService)ServiceManager.GetInstance("jukebox");

				jukebox.MoveSong(fromI, toI);
				return true;
			}
			catch (Exception e)
			{
				logger.Error("Error moving songs: ", e);
				return false;
			}
		}

		private JukeboxStatus CreateJukeboxStatus()
		{
			// Fetch JukeboxService instance
			JukeboxService jukebox = (JukeboxService)ServiceManager.GetInstance("jukebox");

			return new JukeboxStatus(null, JukeboxService.State.ToString(), JukeboxService.CurrentIndex, jukebox.Progress());
		}
	}
}
