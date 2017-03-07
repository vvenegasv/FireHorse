using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FireHorse;
using HtmlAgilityPack;

namespace SimpleScrapper
{
    class Program
    {
        private static DateTime? _emptyQueueTime = null;

        static void Main(string[] args)
        {
            Run();
            Console.ReadKey();
        }

        private static async void Run()
        {
            FireHorseManager.MaxRetryCount = 0;
            var chronometer = new Stopwatch();
            chronometer.Start();

            foreach (var url in Data.URLS)
            {
                var item = new ScraperData();
                item.Url = url;
                item.OnDequeue = OnDequeue;
                item.OnDataArrived = OnDataArrived;
                item.OnThrownException = OnException;
                FireHorseManager.Enqueue(item);
            }

            //System wait until all consumers end and empty queue event has raised
            while (FireHorseManager.CurrentRunningSize > 0 || FireHorseManager.ConsumersResume.Any(x => x.Key == TaskStatus.Running))
            {
                Thread.Sleep(2000);
                PrintData();
            }

            chronometer.Stop();
            
            Console.WriteLine("El proceso tardo {0}", chronometer.Elapsed.TotalSeconds);
            Console.WriteLine("Proceso finalizado. Presione cualquier tecla para finalizar");
        }

        public static void PrintData()
        {
            Console.Clear();
            Console.WriteLine("");
            Console.WriteLine("Elementos en ejecución {0}", FireHorseManager.CurrentRunningSize);
            Console.WriteLine("Elementos en cola {0}", FireHorseManager.CurrentQueueSize);
            Console.WriteLine("Información de consumidores");
            foreach (var a in FireHorseManager.ConsumersResume)
            {
                Console.WriteLine("Estado:{0}\tCantidad:{1}", a.Key, a.Value);
            }
        }

        private static void OnDequeue(string url, IDictionary<string, string> optionalArguments)
        {

        }

        private static void OnDataArrived(string url, IDictionary<string, string> optionalArguments, HtmlDocument htmlDocument)
        {

        }

        private static void OnException(string url, IDictionary<string, string> optionalArguments, Exception ex)
        {

        }
    }
}
