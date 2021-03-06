﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StockTrading.Utility
{
    public sealed class StoplossOrder
    {
        public Guid OrderId { get; private set; }

        public string SecurityCode { get; private set; }

        public float StoplossPrice { get; private set; }

        public int ExistingVolume { get; private set; }

        public int OriginalVolume { get; private set; } 

        public Exchange Exchange { get; private set; }

        public StoplossOrder(string securityCode, float stoplossPrice, int volume)
        {
            if (string.IsNullOrWhiteSpace(securityCode))
            {
                throw new ArgumentNullException();
            }

            if (stoplossPrice < 0.0 || volume <= 0)
            {
                throw new ArgumentException();
            }

            SecurityCode = securityCode;
            StoplossPrice = stoplossPrice;
            OriginalVolume = volume;
            ExistingVolume = volume;

            Exchange = StockTrading.Utility.Exchange.GetTradeableExchangeForSecurity(SecurityCode);

            OrderId = Guid.NewGuid();
        }

        public void UpdateExistingVolume(int soldVolume)
        {
            lock (this)
            {
                ExistingVolume -= soldVolume;

                if (ExistingVolume < 0)
                {
                    throw new InvalidOperationException("Existing volume is impossible to be smaller than 0");
                }
            }
        }
    }
}
