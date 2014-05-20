﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace MetricsDefinition
{
    [Metric("DMI", "PDI,NDI,ADX,ADXR")]
    public sealed class DirectionMovementIndex : Metric
    {
        private int _lookback;
        
        public DirectionMovementIndex(int lookback)
        {
            if (lookback <= 0)
            {
                throw new ArgumentException("lookback must be greater than 0");
            }

            _lookback = lookback;
        }

        public override double[][] Calculate(double[][] input)
        {
 	        if (input == null || input.Length == 0)
            {
                throw new ArgumentNullException("input");
            }

            // DMI can only accept StockData's output as input
            if (input.Length != StockData.FieldCount)
            {
                throw new ArgumentException("DMI can only accept StockData's output as input");
            }

            double[] hp = input[StockData.HighestPriceFieldIndex];
            double[] lp = input[StockData.LowestPriceFieldIndex];
            double[] cp = input[StockData.ClosePriceFieldIndex];

            // calculate +DM and -DM
            double[] pdm = new double[hp.Length];
            double[] ndm = new double[hp.Length];

            pdm[0] = 0.0;
            ndm[0] = 0.0;

            for (int i = 1; i < hp.Length; ++i)
            {
                pdm[i] = Math.Max(0.0, hp[i] - hp[i - 1]);
                ndm[i] = Math.Max(0.0, lp[i - 1] - lp[i]);

                if (pdm[i] > ndm[i])
                {
                    ndm[i] = 0.0;
                }
                else if (pdm[i] < ndm[i])
                {
                    pdm[i] = 0.0;
                }
                else
                {
                    pdm[i] = ndm[i] = 0.0;
                }
            }

            // Calculate +DI and -DI
            double[] tr = new double[hp.Length];
            double[] pdi = new double[hp.Length];
            double[] ndi = new double[hp.Length];

            tr[0] = hp[0] - lp[0];
            pdi[0] = pdm[0] * 100.0 / tr[0];
            ndi[0] = ndm[0] * 100.0 / tr[0];

            for (int i = 1; i < tr.Length; ++i)
            {
                tr[i] = Math.Max(Math.Abs(hp[i] - lp[i]),
                            Math.Max(Math.Abs(hp[i] - cp[i - 1]), Math.Abs(lp[i] - cp[i - 1])));

                pdi[i] = pdm[i] * 100.0 / tr[i];
                ndi[i] = ndm[i] * 100.0 / tr[i];
            }

            // calculate +DIM and -DIM
            double[] mspdm = new MovingSum(_lookback).Calculate(pdm);
            double[] msndm = new MovingSum(_lookback).Calculate(ndm);
            double[] mstr = new MovingSum(_lookback).Calculate(tr);


            double[] pdim = MetricHelper.OperateNew(mspdm, mstr, (p, t) => { return p * 100.0 / t; });
            double[] ndim = MetricHelper.OperateNew(msndm, mstr, (n, t) => { return n * 100.0 / t; });

            // calculate DX and ADX
            double[] dx = MetricHelper.OperateNew(pdim, ndim, (p, n) =>
                {
                    double sum = p + n;

                    return sum == 0.0 ? 0.0 : Math.Abs(p - n) / sum;
                });

            double[] adx = new MovingAverage(_lookback).Calculate(dx);

            // calculate ADXR
            double[] adxr = new double[adx.Length];
            for (int i = 0; i < adxr.Length; ++i)
            {
                if (i < _lookback - 1)
                {
                    adxr[i] = (adx[i] + 0.0) / 2.0;
                }
                else
                {
                    adxr[i] = (adx[i] + adx[i - _lookback + 1]) / 2.0;
                }
            }

            return new double[4][] { pdim, ndim, adx, adxr };
        }
    }
}
