using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FireHorse.Dto;

namespace FireHorse.Mappers
{
    internal static class ScrapperMapper
    {
        public static ScraperDataResponse ToResponse(ScraperData item)
        {
            return new ScraperDataResponse
            {
                Exception = null,
                OptionalArguments = item.OptionalArguments,
                Response = null,
                Url = item.Url,
                Proxy = item.Proxy,
                ScraperType = item.ScraperType
            };
        }

        public static ScraperDataWrapper ToWrapper(ScraperData item, string domain, Uri uri)
        {
            return new ScraperDataWrapper
            {
                Domain = domain,
                Uri = uri,
                Url = item.Url,
                Proxy = item.Proxy,                
                ScraperType = item.ScraperType,
                OptionalArguments = item.OptionalArguments,
                OnThrownException = item.OnThrownException,
                OnDequeue = item.OnDequeue,
                OnDataArrived = item.OnDataArrived
            };
        }
    }
}
