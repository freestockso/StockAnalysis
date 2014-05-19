﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace MetricsDefinition
{
    [Metric("VR")]
    class VolumeRatio : Metric
    {
        private int _lookback;

        public VolumeRatio(int lookback)
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

            // VR can only accept StockData's output as input
            if (input.Length != StockData.FieldCount)
            {
                throw new ArgumentException("VolumeRatio can only accept StockData's output as input");
            }

            double[] cp = input[StockData.ClosePriceFieldIndex];
            double[] volumes = input[StockData.VolumeFieldIndex];

            double[] positiveVolume = new double[volumes.Length];
            double[] negativeVolume = new double[volumes.Length];
            double[] zeroVolume = new double[volumes.Length];

            positiveVolume[0] = 1e-6;
            negativeVolume[0] = 1e-6; // set a very small number to avoid dividing by zero
            zeroVolume[0] = volumes[0];

            for (int i = 1; i < cp.Length; ++i)
            {
                if (cp[i] > cp[i - 1])
                {
                    positiveVolume[i] = volumes[i];
                    negativeVolume[i] = 0.0;
                    zeroVolume[i] = 0.0;
                }
                else if (cp[i] < cp[i - 1])
                {
                    positiveVolume[i] = 0.0;
                    negativeVolume[i] = volumes[i];
                    zeroVolume[i] = 0.0;
                }
                else
                {
                    positiveVolume[i] = 0.0;
                    negativeVolume[i] = 0.0;
                    zeroVolume[i] = volumes[i];
                }
            }

            double sumOfPV = 1e-6;
            double sumOfNV = 1e-6;
            double sumOfZV = 1e-6;

            double[] result = new double[volumes.Length];

            for (int i = 0; i < result.Length; ++i)
            {
                if (i < _lookback)
                {
                    sumOfPV += positiveVolume[i];
                    sumOfNV += negativeVolume[i];
                    sumOfZV += zeroVolume[i];
                }
                else
                {
                    int j = i - _lookback;

                    sumOfPV += positiveVolume[i] - positiveVolume[j];
                    sumOfNV += negativeVolume[i] - negativeVolume[j];
                    sumOfZV += zeroVolume[i] - zeroVolume[j];
                }

                result[i] = (sumOfPV + sumOfZV / 2.0) / (sumOfNV + sumOfZV / 2.0) * 100.0;
            }

            return new double[1][] { result };
        }
    }
}
