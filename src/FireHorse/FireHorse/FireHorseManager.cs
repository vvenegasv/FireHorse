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
        private static Action _onFinish;
        private static bool _canRun;
        private static bool _isRunning;
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<ScraperDataWrapper>> Queues = new ConcurrentDictionary<string, ConcurrentQueue<ScraperDataWrapper>>();
        //Dictionary with DictionaryId and Domain. Util to know how many running elements there is by domain
        private static readonly ConcurrentDictionary<string, string> Running = new ConcurrentDictionary<string, string>(160, 9999);
        //Dictionary with domain and task. It is use to stop and start the process
        private static ConcurrentDictionary<string, Task> _queueThreads = new ConcurrentDictionary<string, Task>();

        /// <summary>
        /// Get or Set max running workers at the same time. Default value is 40
        /// </summary>
        public static int MaxRunningElementsByDomain { get; set; } = 40;

        /// <summary>
        /// Get or Set max retry counts or errors. Default value is 5
        /// </summary>
        public static int MaxRetryCount { get; set; } = 5;

        /// <summary>
        /// Get the amount of elements in running
        /// </summary>
        public static int CurrentRunningSize
        {
            get { return Running.Count; }
        }

        /// <summary>
        /// Get the amount of elements in running by domain
        /// </summary>
        public static IDictionary<string, int> CurrentRunningSizeByDomain
        {
            get
            {
                return Running
                    .GroupBy(x => x.Value)
                    .Select(y => new { Domain = y.Key, Quantity = y.Count() })
                    .ToDictionary(x => x.Domain, x => x.Quantity);
            }
        }

        /// <summary>
        /// Get the amount of elements in queue
        /// </summary>
        public static int CurrentQueueSize
        {
            get { return Queues.Select(x => x.Value.Count).Sum(); }
        }

        /// <summary>
        /// Check if process end. Return true if Queue is empty and there isn't running elements.
        /// </summary>
        /// <returns></returns>
        public static bool IsEnded
        {
            get { return !(FireHorseManager.CurrentRunningSize > 0 || FireHorseManager.CurrentQueueSize > 0); }
        }

        /// <summary>
        /// Check if process is running. It returns true if process is wating for items, no matter if queue is empty or not
        /// </summary>
        public static bool IsActive
        {
            get { return _isRunning; }
        }

        static FireHorseManager()
        {
            _canRun = true;
            //_consumerMainThread = Task.Factory.StartNew(() => Consume());
        }

        /// <summary>
        /// Enqueue a item
        /// </summary>
        /// <exception cref="ArgumentException">If Url or OnDataArrived is not provider</exception>
        /// <param name="data">Item to scraper</param>
        public static void Enqueue(ScraperData data)
        {
            if (string.IsNullOrWhiteSpace(data.Url))
                throw new ArgumentException("URL is required.");

            if (data.OnDataArrived == null)
                throw new ArgumentException("OnDataArrived is required.");

            Uri uri;
            if (!Uri.TryCreate(data.Url, UriKind.RelativeOrAbsolute, out uri))
                throw new ArgumentException("URL '{0}' is invalid", data.Url);

            //gets the domain
            var domain = uri.Authority.ToLower();

            //Check if exists a queue from domain
            if (Queues.Any(x => x.Key == domain))
            {
                var queue = Queues[domain];
                queue.Enqueue(new ScraperDataWrapper()
                {
                    Domain = domain,
                    Uri = uri,
                    Url = data.Url,
                    OptionalArguments = data.OptionalArguments,
                    OnThrownException = data.OnThrownException,
                    OnDequeue = data.OnDequeue,
                    OnDataArrived = data.OnDataArrived
                });
            }
            else
            {
                var queue = new ConcurrentQueue<ScraperDataWrapper>();
                queue.Enqueue(new ScraperDataWrapper()
                {
                    Domain = domain,
                    Uri = uri,
                    Url = data.Url,
                    OptionalArguments = data.OptionalArguments,
                    OnThrownException = data.OnThrownException,
                    OnDequeue = data.OnDequeue,
                    OnDataArrived = data.OnDataArrived
                });
                if (!Queues.TryAdd(domain, queue))
                    throw new Exception("Unexpected error when try to create a new Queue for domain " + domain);

                //start a new queue process
                var t = Task.Factory.StartNew(() => ConsumeFromQueue(queue));
                if (!_queueThreads.TryAdd(domain, t))
                    throw new Exception("Unexpected error when try to add task of queue on QueueThreads for domain " + domain);
            }


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
                        foreach (var queue in Queues)
                        {
                            var t = Task.Factory.StartNew(() => ConsumeFromQueue(queue.Value));
                            _queueThreads.TryAdd(queue.Key, t);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Stop scraper process. Need to be manually started before stop it.
        /// </summary>
        public static void Stop()
        {
            _canRun = false;
            //waits for all queue threads end
            while (_queueThreads.Any(x => x.Value.Status == TaskStatus.Running))
                Thread.Sleep(2000);

            //waits for all running elements finishing
            while (FireHorseManager.CurrentRunningSize > 0)
                Thread.Sleep(2000);

            _isRunning = false;
            _queueThreads = new ConcurrentDictionary<string, Task>();
        }

        private static async void ConsumeFromQueue(ConcurrentQueue<ScraperDataWrapper> queue)
        {
            int throtledCount = 0;
            ScraperDataWrapper item;
            while (_canRun)
            {
                while (queue.TryDequeue(out item))
                {
                    var itemSafeClosure = item;
                    if (Running.Count(x => x.Value == item.Domain) >= MaxRunningElementsByDomain)
                    {
                        throtledCount++;
                        queue.Enqueue(itemSafeClosure);
                        //Thread.Sleep(throtledCount * 50);
                        await Task.Delay(throtledCount * 50);
                        break;
                    }
                    else
                        throtledCount = 0;

                    //Update running elements
                    var runningId = Guid.NewGuid().ToString();
                    if (Running.TryAdd(runningId, item.Domain))
                    {
                        //Get HTML from WebServer
                        Task.Factory.StartNew(() => GetDataFromWebServer(runningId, itemSafeClosure));
                    }
                    else
                    {
                        queue.Enqueue(item);
                    }
                }

                //Sleep process while queue is empty
                if (queue.IsEmpty)
                    Thread.Sleep(3000);
            }
        }

        private static void GetDataFromWebServer(string runningId, ScraperDataWrapper item, int retryCount = 0)
        {
            try
            {
                //Raise on dequeue event
                item.OnDequeue?.Invoke(item.Url, item.OptionalArguments);

                //Get HTML from web server
                var doc = new HtmlDocument();
                using (var webClient = new WebClient())
                {
                    webClient.DownloadStringCompleted += (sender, args) =>
                    {
                        if (args.Error != null)
                        {
                            ExceptionHandlerOnDownloadData(args.Error, item, runningId, retryCount);
                            return;
                        }

                        //Load html
                        doc.LoadHtml(args.Result);

                        //Delete from running
                        RemoveItemFromRunningCollection(item, runningId);

                        //Raise on data arrived event
                        item.OnDataArrived?.Invoke(item.Url, item.OptionalArguments, doc);
                    };

                    webClient.DownloadStringAsync(item.Uri);
                }
            }
            catch (Exception ex)
            {
                ExceptionHandlerOnDownloadData(ex, item, runningId, retryCount);
            }
        }

        private static void ExceptionHandlerOnDownloadData(Exception ex, ScraperDataWrapper item, string runningId, int retryCount)
        {
            if (ex is WebException)
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
            else
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
            string dummyValue;
            if (!Running.TryRemove(key, out dummyValue))
            {
                if (retryCount < MaxRetryCount)
                    RemoveItemFromRunningCollection(item, key, retryCount + 1);
                else
                    item.OnThrownException?.Invoke(item.Url, item.OptionalArguments, new Exception("The scraper data item cannot be deleted from running collection."));
            }

            bool isFinished = !(!FireHorseManager.IsEnded && !FireHorseManager.IsActive);
            if (isFinished)
            {
                _onFinish();
            }
        }

        public static void OnFinish(Action action)
        {
            _onFinish = action;
        }

    }
}