using System;

namespace FireHorse.Dto
{
    internal class ScraperDataWrapper: ScraperData
    {
        public string Domain { get; set; }
        public Uri Uri { get; set; }
    }
}
