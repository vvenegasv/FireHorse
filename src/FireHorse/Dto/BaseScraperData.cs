using System.Collections.Generic;

namespace FireHorse.Dto
{
    public abstract class BaseScraperData
    {
        /// <summary>
        /// Required
        /// Get or Set the URL to getting data
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Optional
        /// Get or Set a usefull dictionary to gather all html pieces of a bigger one.
        /// </summary>
        public IDictionary<string, string> OptionalArguments { get; set; }

        /// <summary>
        /// Required
        /// Get or Set the scraper type.
        /// </summary>
        public ScraperType ScraperType { get; set; }
    }
}
