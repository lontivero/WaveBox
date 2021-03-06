using System;
using System.Collections.Generic;
using WaveBox.Core.Model;

namespace WaveBox.Core.Model.Repository
{
	public interface IVideoRepository
	{
		Video VideoForId(int videoId);
		bool InsertVideo(Video video, bool replace = false);
		IList<Video> AllVideos();
		int CountVideos();
		long TotalVideoSize();
		long TotalVideoDuration();
		IList<Video> SearchVideos(string field, string query, bool exact = true);
		IList<Video> RangeVideos(char start, char end);
		IList<Video> LimitVideos(int index, int duration = Int32.MinValue);
	}
}
