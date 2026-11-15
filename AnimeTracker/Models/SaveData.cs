using System.Collections.Generic;

namespace AnimeTracker.Models
{
    public class SaveData
    {
        public List<Anime> AnimeList { get; set; } = [];
        public List<Anime> TopList { get; set; } = [];
    }
}