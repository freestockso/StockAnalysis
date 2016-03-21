﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using StockAnalysis.Share;

using log4net;

namespace StockTrading.Utility
{
    public sealed class SellOrderManager
    {
        private static SellOrderManager _instance = null;

        private object _orderLockObj = new object();

        private IDictionary<string, List<SellOrder>> _activeOrders = new Dictionary<string, List<SellOrder>>();

        private HashSet<DispatchedOrder> _dispatchedOrders = new HashSet<DispatchedOrder>();

        public delegate void OnSellOrderExecutedDelegate(SellOrder order, float dealPrice, int dealVolume);

        public OnSellOrderExecutedDelegate OnSellOrderExecuted { get; set; }

        public static SellOrderManager GetInstance()
        {
            if (_instance == null)
            {
                lock (typeof(SellOrderManager))
                {
                    if (_instance == null)
                    {
                        _instance = new SellOrderManager();
                    }
                }
            }

            return _instance;
        }

        private SellOrderManager()
        {
            CtpSimulator.GetInstance().RegisterQuoteReadyCallback(OnQuoteReady);
            CtpSimulator.GetInstance().RegisterOrderStatusChangedCallback(OnOrderStatusChanged);
        }

        private bool ShouldSell(FiveLevelQuote quote, float maxBuyPrice, float minBuyPrice, int totalBuyVolume, SellOrder order)
        {
            bool shouldSell = false;

            if (order.SellPrice < minBuyPrice)
            {
                shouldSell = true;
            }
            else
            {
                if (order.SellPrice <= maxBuyPrice)
                {
                    // order sell price is between minBuyPrice and maxBuyPrice
                    // we count the buy volume above sell price.
                    int aboveSellPriceBuyVolume =
                        ChinaStockHelper.ConvertHandToVolume(
                            Enumerable
                                .Range(0, quote.BuyPrices.Length)
                                .Where(index => quote.BuyPrices[index] >= order.SellPrice)
                                .Sum(index => quote.BuyVolumesInHand[index]));

                    if (aboveSellPriceBuyVolume >= order.RemainingVolume)
                    {
                        shouldSell = true;
                    }
                }
            }

            return shouldSell;
        }

        private void OnQuoteReady(FiveLevelQuote[] quotes, string[] errors)
        {
            if (quotes == null || quotes.Length == 0)
            {
                return;
            }

            lock (_orderLockObj)
            {
                if (_activeOrders.Count == 0)
                {
                    return;
                }
            }

            for (int i = 0; i < quotes.Length; ++i)
            {
                var quote = quotes[i];

                if (quote == null)
                {
                    continue;
                }

                lock (_orderLockObj)
                {
                    List<SellOrder> orders;
                    if (!_activeOrders.TryGetValue(quote.SecurityCode, out orders))
                    {
                        continue;
                    }
                    
                    // copy orders to avoid the "orders" object being updated in SendStoplossOrder function.
                    var OrderCopies = orders.ToArray();

                    float maxBuyPrice = quote.BuyPrices.Max();
                    float minBuyPrice = quote.BuyPrices.Min();
                    int totalBuyVolume = ChinaStockHelper.ConvertHandToVolume(quote.BuyVolumesInHand.Sum());

                    foreach (var order in OrderCopies)
                    {
                        if (ShouldSell(quote, maxBuyPrice, minBuyPrice, totalBuyVolume, order))
                        {
                            SendSellOrder(order);
                        }
                    }
                }
            }
        }

        private void SendSellOrder(SellOrder order)
        {
            // put it into thread pool to avoid recursively call QueryOrderStatusForcibly and 
            // then call OnOrderStatusChanged() and then call SendSellOrder recursively.
            ThreadPool.QueueUserWorkItem(SendSellOrderWorkItem, order);
        }

        private void SendSellOrderWorkItem(object state)
        {
            SellOrder order = (SellOrder)state;

            OrderRequest request = new OrderRequest(order)
            {
                Category = OrderCategory.Sell,
                Price = order.SellPrice,
                PricingType = OrderPricingType.MarketPriceMakeDealInFiveGradesThenCancel,
                Volume = order.RemainingVolume,
                SecurityCode = order.SecurityCode,
            };

            string error;
            DispatchedOrder dispatchedOrder = CtpSimulator.GetInstance().DispatchOrder(request, out error);

            if (dispatchedOrder == null)
            {
                Logger.ErrorLogger.ErrorFormat(
                    "Exception in dispatching sell order: id {0} code {1} sell price {2}, volume {3}. Error: {4}",
                    order.OrderId,
                    order.SecurityCode,
                    order.SellPrice,
                    order.RemainingVolume,
                    error);
            }
            else
            {
                Logger.InfoLogger.InfoFormat(
                    "Dispatched sell order: id {0} code {1} sell price {2}, volume {3}.",
                    order.OrderId,
                    order.SecurityCode,
                    order.SellPrice,
                    order.RemainingVolume);

                lock (_orderLockObj)
                {
                    RemoveActiveStoplossOrder(order);
                    AddDispatchedOrder(dispatchedOrder);
                }

                // force query order status.
                CtpSimulator.GetInstance().QueryOrderStatusForcibly();
            }
        }

        private void OnOrderStatusChanged(DispatchedOrder dispatchedOrder)
        {
            if (dispatchedOrder == null)
            {
                return;
            }

            lock (_orderLockObj)
            {
                if (!IsDispatchedOrder(dispatchedOrder))
                {
                    return;
                }
            }

            if (dispatchedOrder.Request.AssociatedObject != null)
            {
                SellOrder order = dispatchedOrder.Request.AssociatedObject as SellOrder;

                if (order == null)
                {
                    return;
                }

                if (TradingHelper.IsFinalStatus(dispatchedOrder.LastStatus))
                {
                    lock (_orderLockObj)
                    {
                        Logger.InfoLogger.InfoFormat(
                             "Sell order executed:  id {0} code {1} status {2} succeeded volume {3} deal price {4}.",
                             order.OrderId,
                             order.SecurityCode,
                             dispatchedOrder.LastStatus,
                             dispatchedOrder.LastDealVolume,
                             dispatchedOrder.LastDealPrice);
 

                        if (dispatchedOrder.LastDealVolume > 0)
                        {
                            order.Fulfill(dispatchedOrder.LastDealPrice, dispatchedOrder.LastDealVolume);

                            // callback to client to notify partial or full success
                            if (OnSellOrderExecuted != null)
                            {
                                OnSellOrderExecuted(order, dispatchedOrder.LastDealPrice, dispatchedOrder.LastDealVolume);
                            }
                        }

                        RemoveDispatchedOrder(dispatchedOrder);

                        if (!order.IsCompleted())
                        {
                            // the order has not been finished yet, put it back into active order
                            AddActiveStoplossOrder(order);
                        }
                    }
                }
            }
        }

        public void RegisterStoplossOrder(SellOrder order)
        {
            if (order == null)
            {
                throw new ArgumentNullException();
            }

            lock (_orderLockObj)
            {
                AddActiveStoplossOrder(order);
            }
        }

        public bool UnregisterStoplossOrder(SellOrder order)
        {
            if (order == null)
            {
                throw new ArgumentNullException();
            }

            lock (_orderLockObj)
            {
                return RemoveActiveStoplossOrder(order);
            }
        }

        private void AddActiveStoplossOrder(SellOrder order)
        {
            if (!_activeOrders.ContainsKey(order.SecurityCode))
            {
                _activeOrders.Add(order.SecurityCode, new List<SellOrder>());

                CtpSimulator.GetInstance().SubscribeQuote(order.SecurityCode);
            }

            _activeOrders[order.SecurityCode].Add(order);
        }

        private bool RemoveActiveStoplossOrder(SellOrder order)
        {
            List<SellOrder> orders;
            if (!_activeOrders.TryGetValue(order.SecurityCode, out orders))
            {
                return false;
            }

            bool removeSucceeded = orders.Remove(order);

            if (orders.Count == 0)
            {
                _activeOrders.Remove(order.SecurityCode);

                CtpSimulator.GetInstance().UnsubscribeQuote(order.SecurityCode);
            }

            return removeSucceeded;
        }

        private void AddDispatchedOrder(DispatchedOrder order)
        {
            _dispatchedOrders.Add(order);
        }

        private bool RemoveDispatchedOrder(DispatchedOrder order)
        {
            return _dispatchedOrders.Remove(order);
        }

        private bool IsDispatchedOrder(DispatchedOrder order)
        {
            return _dispatchedOrders.Contains(order);
        }
    }
}