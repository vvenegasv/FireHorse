using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace FireHorse
{
    public static class FireHorseManager
    {
        private static bool _canRun;
        private static bool _isRunning;
        private static Task _consumerMainThread;
        private static ConcurrentBag<Task> _consumers = new ConcurrentBag<Task>();
        private static readonly ConcurrentQueue<ScraperData> Queue = new ConcurrentQueue<ScraperData>();
        private static readonly ConcurrentDictionary<string, short> Running = new ConcurrentDictionary<string, short>(40, 40);
        private static readonly ConcurrentDictionary<string, Action> EmptyQueueSubscriptions = new ConcurrentDictionary<string, Action>();
        
        /// <summary>
        /// Get or Set max running workers at the same time. Default value is 40
        /// </summary>
        public static int MaxRunningElements { get; set; } = 40;

        /// <summary>
        /// Get or Set max retry counts or errors. Default value is 5
        /// </summary>
        public static int MaxRetryCount { get; set; } = 5;

        /// <summary>
        /// Get the state of main thread of web scraper task
        /// </summary>
        public static TaskStatus Status
        {
            get { return _consumerMainThread.Status; }
        }

        /// <summary>
        /// Get the resume of current workers
        /// </summary>
        public static IDictionary<TaskStatus, int> ConsumersResume
        {
            get
            {
                return _consumers
                    .GroupBy(x => x.Status)
                    .Select(y => new {StatusName = y.Key, Quantity = y.Count()})
                    .ToDictionary(x => x.StatusName, x => x.Quantity);
            }
        }

        /// <summary>
        /// Get the detail of current workers
        /// </summary>
        public static IDictionary<int, TaskStatus> ConsumersDetails
        {
            get { return _consumers.ToDictionary(x => x.Id, x => x.Status); }
        }
        

        static FireHorseManager()
        {
            _canRun = true;
            _consumerMainThread = Task.Factory.StartNew(() => Consume());
        }

        /// <summary>
        /// Enqueue a item
        /// </summary>
        /// <exception cref="ArgumentException">If Url or OnDataArrived is not provider</exception>
        /// <param name="data">Item to scraper</param>
        public static void Enqueue(ScraperData data)
        {
            if(string.IsNullOrWhiteSpace(data.Url))
                throw new ArgumentException("URL is required.");

            if(data.OnDataArrived == null)
                throw new ArgumentException("OnDataArrived is required.");

            Queue.Enqueue(data);
        }

        /// <summary>
        /// Start scraper process. By default, the scraper will start automatically and never stop
        /// </summary>
        public static void Start()
        {
            _canRun = true;
            var lockerObj = new Object();
            if (!_isRunning)
            {
                lock (lockerObj)
                {
                    if (!_isRunning)
                    {
                        _isRunning = true;
                        _consumerMainThread = Task.Factory.StartNew(() => Consume());
                    }
                }
            }
                
        }

        /// <summary>
        /// Stop scraper process. Need to be manually started before stop it. It clear the current worker list
        /// </summary>
        public static void Stop()
        {
            _canRun = false;
            while(_consumers.Any(x => x.Status == TaskStatus.Running) || _consumerMainThread.Status != TaskStatus.RanToCompletion)
                Thread.Sleep(1000);

            _consumers = new ConcurrentBag<Task>();
        }

        /// <summary>
        /// Subscribe to an event that is raised when queue is empty
        /// </summary>
        /// <param name="callback">Function to call when this event is fired</param>
        /// <returns>Key to unsuscribe to this event</returns>
        public static string SubscribeToEmptyQueue(Action callback)
        {
            var key = Guid.NewGuid().ToString();
            EmptyQueueSubscriptions.TryAdd(key, callback);
            return key;
        }

        /// <summary>
        /// Unsubscribe to empty queue event
        /// </summary>
        /// <param name="key">Key of subscription returned on SubscribeToEmptyQueue method</param>
        public static void UnsubscribeToEmptyQueue(string key)
        {
            Action dummyCallback;
            EmptyQueueSubscriptions.TryRemove(key, out dummyCallback);
        }




        private static void Consume()
        {
            _isRunning = true;
            ScraperData item;
            int throtledCount = 0;
            while (_canRun)
            {
                while (Queue.TryDequeue(out item))
                {
                    var itemSafeClosure = item;
                    if (Running.Count >= MaxRunningElements)
                    {
                        throtledCount++;
                        Queue.Enqueue(itemSafeClosure);
                        Thread.Sleep(throtledCount * 50);
                        break;
                    }
                    else
                        throtledCount = 0;

                    //Update running elements
                    var runningId = Guid.NewGuid().ToString();
                    if (Running.TryAdd(runningId, 0))
                    {
                        //Get HTML from WebServer
                        var t = Task.Factory.StartNew(() => GetDataFromWebServer(runningId, itemSafeClosure));
                        _consumers.Add(t);
                    }
                    else
                    {
                        Queue.Enqueue(item);
                    }
                }

                if (!Queue.Any())
                {
                    Thread.Sleep(3000);
                    if (!Queue.Any())
                        NotifyQueueIsEmpty();
                }
            }
            _isRunning = false;
        }

        private static void GetDataFromWebServer(string runningId, ScraperData item, int retryCount = 0)
        {
            try
            {
                //Raise on dequeue event
                item.OnDequeue?.Invoke(item.Url, item.OptionalArguments);

                //Get HTML from web server
                var doc = new HtmlDocument();
                using (var webClient = new WebClient())
                {
                    string page = webClient.DownloadString(item.Url);
                    doc.LoadHtml(page);
                }

                //Raise on data arrived event
                item.OnDataArrived?.Invoke(item.Url, item.OptionalArguments, doc);
                
            }
            catch (WebException ex)
            {
                if (retryCount < MaxRetryCount)
                {
                    Thread.Sleep(2000);
                    GetDataFromWebServer(runningId, item, retryCount + 1);
                }
                else
                {
                    if (item.OnThrownException != null)
                    {
                        item.OnThrownException?.Invoke(item.Url, item.OptionalArguments, ex);
                        RemoveItemFromRunningCollection(item, runningId);
                    }
                }
            }
            catch (Exception ex)
            {
                if (item.OnThrownException != null)
                {
                    item.OnThrownException?.Invoke(item.Url, item.OptionalArguments, ex);
                    RemoveItemFromRunningCollection(item, runningId);
                }
            }
        }
        
        private static void RemoveItemFromRunningCollection(ScraperData item, string key, int retryCount = 0)
        {
            short dummyValue;
            if (!Running.TryRemove(key, out dummyValue))
            {
                if (retryCount < MaxRetryCount)
                    RemoveItemFromRunningCollection(item, key, retryCount + 1);
                else
                    item.OnThrownException?.Invoke(item.Url, item.OptionalArguments, new Exception("The scraper data item cannot be deleted from running collection."));
            }
        }

        private static void NotifyQueueIsEmpty()
        {
            foreach (var subscription in EmptyQueueSubscriptions)
                subscription.Value?.Invoke();
        }
    }
}
