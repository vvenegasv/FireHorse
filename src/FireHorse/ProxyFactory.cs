using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FireHorse
{
    public class ProxyFactory
    {
        private static volatile ProxyFactory _instance;
        private static object _syncRoot = new Object();

        public static ProxyFactory Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                            _instance = new ProxyFactory();
                    }
                }
                return _instance;
            }
        }

        private ProxyFactory() { }

        public WebProxy GetWithDefaultCredentials(string proxyUrl)
        {
            var proxy = new WebProxy(proxyUrl, false);
            proxy.Credentials = CredentialCache.DefaultCredentials;
            return proxy;
        }
    }
}
