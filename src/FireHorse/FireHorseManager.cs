
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FireHorse.Dto;
using FireHorse.Mappers;

namespace FireHorse
{
    public class FireHorseManager
    {
        private static volatile FireHorseManager _instance;
        private static object _syncRoot = new Object();

        private bool _canRun;
        private bool _isRunning;
        private readonly Object LockerObj = new Object();
        //Dictionary with domain and concurrent queue.
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ScraperDataWrapper>> Queues = new ConcurrentDictionary<string, ConcurrentQueue<ScraperDataWrapper>>();
        //Dictionary with DictionaryId and Domain. Util to know how many running elements there is by domain
        private readonly ConcurrentDictionary<string, string> Running = new ConcurrentDictionary<string, string>(160, 9999);
        //Dictionary with subscription to notify when process ends
        private readonly ConcurrentDictionary<string, Action> SubscriptionOnEnd = new ConcurrentDictionary<string, Action>();
        //Dictionary with domain and task. It is use to stop and start the process
        private ConcurrentDictionary<string, Task> _queueThreads = new ConcurrentDictionary<string, Task>();

        /// <summary>
        /// Retorna la instancia de la clase
        /// </summary>
        public static FireHorseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                            _instance = new FireHorseManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Get or Set max running workers at the same time. Default value is 40
        /// </summary>
        public int MaxRunningElementsByDomain { get; set; } = 40;

        /// <summary>
        /// Get or Set max retry counts or errors. Default value is 5
        /// </summary>
        public int MaxRetryCount { get; set; } = 5;
        
        /// <summary>
        /// Get the amount of elements in running
        /// </summary>
        public int CurrentRunningSize
        {
            get { return Running.Count; }
        }

        /// <summary>
        /// Get the amount of elements in running by domain
        /// </summary>
        public IDictionary<string, int> CurrentRunningSizeByDomain
        {
            get
            {
                return Running
                    .GroupBy(x => x.Value)
                    .Select(y => new {Domain = y.Key, Quantity = y.Count()})
                    .ToDictionary(x => x.Domain, x => x.Quantity);
            }
        }

        /// <summary>
        /// Get the amount of elements in queue
        /// </summary>
        public int CurrentQueueSize
        {
            get { return Queues.Select(x => x.Value.Count).Sum(); }
        }

        public int CurrentQueues
        {
            get { return Queues.Count; }
        }

        /// <summary>
        /// Check if process end. Return true if Queue is empty and there isn't running elements.
        /// </summary>
        /// <returns></returns>
        public bool IsEnded
        {
            get { return Queues.IsEmpty && Running.IsEmpty; }
        }

        /// <summary>
        /// Check if process is running. It returns true if process is wating for items, no matter if queue is empty or not
        /// </summary>
        public bool IsActive
        {
            get { return _isRunning; }
        }

        private FireHorseManager()
        {
            _canRun = true;
            _isRunning = true;
        }

        /// <summary>
        /// Enqueue a response. Start process if was manually stoped
        /// </summary>
        /// <exception cref="ArgumentException">If Url or OnDataArrived is not provider</exception>
        /// <param name="data">Item to scraper</param>
        public void Enqueue(ScraperData data)
        {
            if (string.IsNullOrWhiteSpace(data.Url))
                throw new ArgumentException("URL is required.");

            if (data.OnDataArrived == null)
                throw new ArgumentException("OnDataArrived is required.");

            Uri uri;
            if(!Uri.TryCreate(data.Url, UriKind.RelativeOrAbsolute, out uri))
                throw new ArgumentException("URL '{0}' is invalid", data.Url);

            //gets the domain
            var domain = uri.Authority.ToLower();

            //If enqueue method was called in parallel, with no lock
            //could exists multiple consume threads for the same domain
            //With lock we fix this problem.
            lock (LockerObj)
            {
                //Check if exists a queue from domain
                if (Queues.Any(x => x.Key == domain))
                {
                    var queue = Queues[domain];
                    queue.Enqueue(ScrapperMapper.ToWrapper(data, domain, uri));
                }
                else
                {
                    var queue = new ConcurrentQueue<ScraperDataWrapper>();
                    queue.Enqueue(ScrapperMapper.ToWrapper(data, domain, uri));
                    if (!Queues.TryAdd(domain, queue))
                        if (!Queues.Any(x => x.Key == domain))
                            throw new Exception("Unexpected error when try to create a new Queue for domain " + domain);

                    //start a new queue process
                    var t = Task.Factory.StartNew(() => ConsumeFromQueue(domain, queue));
                    if (!_queueThreads.TryAdd(domain, t))
                        if (!_queueThreads.Any(x => x.Key == domain))
                            throw new Exception("Unexpected error when try to add a task of queue on QueueThreads for domain " + domain);
                }
            }
        }

        /// <summary>
        /// Start scraper process. By default, the scraper will start automatically and never stop
        /// </summary>
        public void Start()
        {
            _canRun = true;
            if (!_isRunning)
            {
                lock (LockerObj)
                {
                    if (!_isRunning)
                    {
                        _isRunning = true;
                        foreach (var queue in Queues)
                        {
                            var t = Task.Factory.StartNew(() => ConsumeFromQueue(queue.Key, queue.Value));
                            _queueThreads.TryAdd(queue.Key, t);
                        }
                    }
                }
            }

        }

        /// <summary>
        /// Stop scraper process. Need to be manually started before stop it. 
        /// Also, it will be automatically started when put a new response in queue
        /// </summary>
        public void Stop()
        {
            _canRun = false;
            //waits for all queue threads end
            while(_queueThreads.Any(x => x.Value.Status == TaskStatus.Running))
                Thread.Sleep(2000);

            //waits for all running elements finishing
            while(CurrentRunningSize > 0)
                Thread.Sleep(2000);

            _isRunning = false;
            _queueThreads = new ConcurrentDictionary<string, Task>();
        }

        /// <summary>
        /// Subscribe to an event that will be fired when queue is empty and there is no more items to process
        /// </summary>
        /// <exception cref="ArgumentNullException">If subscription is null</exception>
        /// <exception cref="Exception">If cannot be add a new element in subscription list</exception>
        /// <returns></returns>
        public string SubscribeToEndProcess(Action subscription)
        {
            if(subscription==null)
                throw new ArgumentNullException("subscription", "The subscription parameter is required. It cannot be null");

            var key = Guid.NewGuid().ToString();
            if(!SubscriptionOnEnd.TryAdd(key, subscription))
                throw new Exception("Unable to add new subscription for end process");
            return key;
        }

        /// <summary>
        /// Unsuscribe to end process event
        /// </summary>
        /// <exception cref="ArgumentNullException">If key is null or empty</exception>
        /// <exception cref="ArgumentException">If key provided not exists in subscription list</exception>
        /// <exception cref="Exception">If cannot be remove the element of subscription list</exception>
        /// <param name="key">Key of subscription returned by SubscribeToEndProcess</param>
        public void UnsuscribeToEndProcess(string key)
        {
            if(string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException("key", "The key parameter is requiered. It cannot be empty or null");

            if (!SubscriptionOnEnd.Any(x => x.Key == key))
                throw new ArgumentException("The key parameter does not exists in subscription list", "key");

            Action dummyAction;
            if(!SubscriptionOnEnd.TryRemove(key, out dummyAction))
                throw new Exception("Unable to remove the subscription for key " + key);
        }

        
        private async void ConsumeFromQueue(string domain, ConcurrentQueue<ScraperDataWrapper> queue)
        {
            int throtledCount = 0;
            int emptyQueueCount = 0;
            ScraperDataWrapper item;
            while (_canRun)
            {
                while (queue.TryDequeue(out item))
                {
                    emptyQueueCount = 0;

                    var itemSafeClosure = item;
                    if (Running.Count(x => x.Value == item.Domain) >= MaxRunningElementsByDomain)
                    {
                        throtledCount++;
                        queue.Enqueue(itemSafeClosure);
                        await Task.Delay(throtledCount*50);
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
                {
                    //try to remove the queue if it's empty
                    if (emptyQueueCount > MaxRetryCount)
                    {
                        //Send a signal to inform that process is out
                        OnConsumeFromQueueOut(domain);
                        return;
                    }
                    else
                    {
                        emptyQueueCount++;
                        await Task.Delay(3000);
                    }
                }
            }

            //Send a signal to inform that the process is out
            OnConsumeFromQueueOut(domain);
        }

        /// <summary>
        /// Remove the queue from Queues. 
        /// </summary>
        /// <param name="domain">Domain</param>
        private void OnConsumeFromQueueOut(string domain)
        {
            Task.Factory.StartNew(() =>
            {
                ConcurrentQueue<ScraperDataWrapper> dummyQueue;
                lock (LockerObj)
                {
                    //Check if queue exists
                    if (Queues.Any(x => x.Key == domain))
                    {
                        //Check if queue is empty
                        if (Queues[domain].IsEmpty)
                        {
                            if (Queues.TryRemove(domain, out dummyQueue))
                            {
                                Task t;
                                _queueThreads.TryRemove(domain, out t);
                            }
                        }
                    }
                }
                CheckIfProcessIsFinished();
            });
        }

        private void GetDataFromWebServer(string runningId, ScraperDataWrapper item, int retryCount = 0)
        {
            try
            {
                //Create response object
                var response = ScrapperMapper.ToResponse(item);

                //Raise on dequeue event
                item.OnDequeue?.Invoke(response);

                switch (item.ScraperType)
                {
                    case ScraperType.String:
                        ProcessAsHtml(runningId, item, response, retryCount);
                        return;
                    case ScraperType.Binary:
                        ProcessAsBinary(runningId, item, response, retryCount);
                        return;
                    default:
                        throw new Exception("ScraperType " + item.ScraperType + " not valid");
                }

            }
            catch (Exception ex)
            {
                ExceptionHandlerOnDownloadData(ex, item, runningId, retryCount);
            }
        }

        private void ProcessAsHtml(string runningId, ScraperDataWrapper wrapper, ScraperDataResponse response, int retryCount)
        {
            //Get HTML from web server
            using (var webClient = new WebClient())
            {
                webClient.DownloadStringCompleted += (sender, args) =>
                {
                    if (args.Error != null)
                    {
                        ExceptionHandlerOnDownloadData(args.Error, wrapper, runningId, retryCount);
                        return;
                    }

                    //Delete from running
                    RemoveItemFromRunningCollection(wrapper, runningId);

                    //Set response
                    response.Response = args.Result;

                    //Raise on data arrived event
                    wrapper.OnDataArrived?.Invoke(response);

                    //Raise a signal to notify that process was end
                    CheckIfProcessIsFinished();
                };

                webClient.DownloadStringAsync(wrapper.Uri);
            }
        }

        private void ProcessAsBinary(string runningId, ScraperDataWrapper wrapper, ScraperDataResponse response, int retryCount)
        {
            //Get HTML from web server
            using (var webClient = new WebClient())
            {
                webClient.DownloadDataCompleted += (sender, args) =>
                {
                    if (args.Error != null)
                    {
                        ExceptionHandlerOnDownloadData(args.Error, wrapper, runningId, retryCount);
                        return;
                    }

                    //Delete from running
                    RemoveItemFromRunningCollection(wrapper, runningId);

                    //Set response
                    response.Response = args.Result;

                    //Raise on data arrived event
                    wrapper.OnDataArrived?.Invoke(response);

                    //Raise a signal to notify that process was end
                    CheckIfProcessIsFinished();
                };

                webClient.DownloadDataAsync(wrapper.Uri);
            }
        }

        private void CheckIfProcessIsFinished()
        {
            if (Queues.IsEmpty && Running.IsEmpty)
                NotifyEndProcess();
        }

        private void ExceptionHandlerOnDownloadData(Exception ex, ScraperDataWrapper item, string runningId, int retryCount)
        {
            var response = ScrapperMapper.ToResponse(item);
            response.Exception = ex;

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
                        item.OnThrownException?.Invoke(response);
                        RemoveItemFromRunningCollection(item, runningId);
                    }
                }
            }
            else
            {
                if (item.OnThrownException != null)
                {
                    item.OnThrownException?.Invoke(response);
                    RemoveItemFromRunningCollection(item, runningId);
                }
            }
        }

        private void RemoveItemFromRunningCollection(ScraperData item, string key, int retryCount = 0)
        {
            string dummyValue;
            var response = ScrapperMapper.ToResponse(item);

            if (!Running.TryRemove(key, out dummyValue))
            {
                if (retryCount < MaxRetryCount)
                    RemoveItemFromRunningCollection(item, key, retryCount + 1);
                else
                {
                    response.Exception = new Exception("The scraper data response cannot be deleted from running collection.");
                    item.OnThrownException?.Invoke(response);
                }
            }
        }

        private void NotifyEndProcess()
        {
            foreach (var subscription in SubscriptionOnEnd)
            {
                subscription.Value.Invoke();
            }
        }
    }
}