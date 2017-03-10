using System;

namespace FireHorse.Dto
{
    public class ScraperDataResponse: BaseScraperData
    {
        /// <summary>
        /// Get or Set the response from web server. It must be casted
        /// depending on ScraperType defined
        /// </summary>
        public Object Response { get; set; }

        /// <summary>
        /// Get or Set the exception raised when download data from web server
        /// </summary>
        public Exception Exception { get; set; }
    }
}
