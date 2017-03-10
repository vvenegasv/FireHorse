namespace FireHorse.Dto
{
    public class ScraperData: BaseScraperData
    {
        public delegate void DequeueEvent(ScraperDataResponse response);
        public delegate void DataArrivedEvent(ScraperDataResponse response);
        public delegate void ThrownExceptionEvent(ScraperDataResponse response);
        
        /// <summary>
        /// Optional
        /// This event is fired when element is dequeue
        /// </summary>
        public DequeueEvent OnDequeue { get; set; }

        /// <summary>
        /// Required
        /// This event is fired when a HTTP response is recived from web server
        /// </summary>
        public DataArrivedEvent OnDataArrived { get; set; }

        /// <summary>
        /// Optional
        /// This event is fired when an error appears
        /// </summary>
        public ThrownExceptionEvent OnThrownException { get; set; }
    }
}
