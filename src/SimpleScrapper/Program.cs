using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FireHorse;
using FireHorse.Dto;
using HtmlAgilityPack;

namespace SimpleScrapper
{
    class Program
    {
        private static int _readCount = 0;
        private static int _errorCount = 0;
        private static int _totalElementsCount = 1;
        private static Stopwatch _chronometer;
        private static bool _wasFinish;
        private static AutoResetEvent _waitHandle = new AutoResetEvent(false);
        private static FireHorseManager _fireHorse;

        static void Main(string[] args)
        {
            Run();
        }

        private static void Run()
        {
            _wasFinish = false;
            _fireHorse = FireHorseManager.Instance;
            var subscriptionKey = _fireHorse.SubscribeToEndProcess(OnFinish);

            _fireHorse.MaxRetryCount = 0;
            _chronometer = new Stopwatch();
            _chronometer.Start();

            
            foreach (var url in Data.URLS.Where(x => !x.Contains("aguasantofagasta")))
            {
                var item = new ScraperData();
                item.Url = url;
                item.OnDequeue = OnDequeue;
                item.OnDataArrived = OnDataArrived;
                item.OnThrownException = OnException;
                item.ScraperType = ScraperType.String;
                _fireHorse.Enqueue(item);
                _totalElementsCount++;
            }



            _fireHorse.Enqueue(new ScraperData
            {
                Url = Data.URLFILE,
                OnDequeue = OnDequeue,
                OnDataArrived = OnDataArrived,
                OnThrownException = OnException,
                ScraperType = ScraperType.Binary
            });

            Task.Factory.StartNew(() => PrintData());

            //Waits for an event
            _waitHandle.WaitOne();
            
        }

        public static async void PrintData()
        {
            while (!_wasFinish)
            {
                Console.Clear();
                Console.WriteLine("");
                Console.WriteLine("Elementos leidos {0}", _readCount);
                Console.WriteLine("Elementos con errores {0}", _errorCount);
                Console.WriteLine("Elementos finalizados {0}", _readCount + _errorCount);
                Console.WriteLine("Elementos ingresados en queue {0}", _totalElementsCount);
                Console.WriteLine("Elementos en ejecución {0}", _fireHorse.CurrentRunningSize);
                Console.WriteLine("Cantidad de colas {0}", _fireHorse.CurrentQueues);
                Console.WriteLine("Elementos en cola {0}", _fireHorse.CurrentQueueSize);

                Console.WriteLine("Detalle de Colas");
                Console.WriteLine("==================");
                foreach (var item in _fireHorse.CurrentRunningSizeByDomain)
                {
                    Console.WriteLine("Dominio:{0}, Cantidad:{1}", item.Key, item.Value);
                }

                await Task.Delay(2000);
            }
        }

        private static void OnDequeue(ScraperDataResponse response)
        {

        }

        private static void OnDataArrived(ScraperDataResponse response)
        {
            switch (response.ScraperType)
            {
                case ScraperType.Binary:
                    var data = (byte[]) response.Response;
                    break;
                case ScraperType.String:
                    var html = (string) response.Response;
                    break;
                default:
                    throw new Exception("Tipo de scraper inválido");
            }
            _readCount++;
        }

        private static void OnException(ScraperDataResponse response)
        {
            _errorCount++;
        }

        private static void OnFinish()
        {
            _chronometer.Stop();
            _wasFinish = true;
            Console.WriteLine("El proceso tardo {0}", _chronometer.Elapsed.TotalSeconds);
            Console.WriteLine("Proceso finalizado. Presione cualquier tecla para finalizar");
            _waitHandle.Set();
        }
    }
}
