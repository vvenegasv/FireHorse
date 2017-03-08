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
        static void Main(string[] args)
        {
            Run();
            Console.ReadKey();
        }

        private static void Run()
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
            //while (!FireHorseManager.IsEnded && !FireHorseManager.IsActive)
            //{
            //    Thread.Sleep(2000);
            //    PrintData();
            //}

            FireHorseManager.OnFinish(() => Console.Write("proceso finalizado"));



            //chronometer.Stop();

            //Console.WriteLine("El proceso tardo {0}", chronometer.Elapsed.TotalSeconds);
            //Console.WriteLine("Proceso finalizado. Presione cualquier tecla para finalizar");
        }

        public static void PrintData()
        {
            Console.Clear();
            Console.WriteLine("");
            Console.WriteLine("Elementos en ejecución {0}", FireHorseManager.CurrentRunningSize);
            //foreach (var item in FireHorseManager.CurrentRunningSizeByDomain)
            //{
            //    Console.WriteLine("Dominio:{0}, Cantidad:{1}", item.Key, item.Value);
            //}
            Console.WriteLine("Elementos en cola {0}", FireHorseManager.CurrentQueueSize);
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
