using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FireHorse
{
    public class ScraperData
    {
        public delegate void DequeueEvent(string url, IDictionary<string, string> optionalArguments);
        public delegate void DataArrivedEvent(string url, IDictionary<string, string> optionalArguments, HtmlDocument htmlDocument);
        public delegate void ThrownExceptionEvent(string url, IDictionary<string, string> optionalArguments, Exception ex);
        public Action FinishTask { get; set; }

        /// <summary>
        /// Required
        /// This property holds the URL to getting data
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Optional
        /// This dictionary is usefull to gather all html pieces of a bigger one.
        /// </summary>
        public IDictionary<string, string> OptionalArguments { get; set; }

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
