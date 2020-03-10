﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eye_tracking_mouse
{

    // Takes average of closest corrections.
    class CalibrationManagerV0 : ICalibrationManager
    {
        public Point GetShift(ShiftPosition cursor_position)
        {
            lock (Helpers.locker)
            {
                shift_storage.calibration_window?.OnCursorPositionUpdate(cursor_position);

                var closest_corrections = Helpers.CalculateClosestCorrectionsInfo(shift_storage, cursor_position, Options.Instance.calibration_mode.considered_zones_count);
                if (closest_corrections == null)
                {
                    Debug.Assert(shift_storage.Corrections.Count() == 0);
                    return new Point(0, 0);
                }

                double sum_of_reverse_distances = 0;
                foreach (var index in closest_corrections)
                {
                    sum_of_reverse_distances += (1 / index.distance);
                }

                foreach (var correction in closest_corrections)
                {
                    correction.weight = 1 / correction.distance / sum_of_reverse_distances;
                }

                if (shift_storage.calibration_window != null)
                {
                    var lables = new List<Tuple<string /*text*/, int /*correction index*/>>();
                    foreach (var correction in closest_corrections)
                        lables.Add(new Tuple<string, int>((int)(correction.weight * 100) + "%", correction.index));
                    shift_storage.calibration_window.UpdateCorrectionsLables(lables);
                }

                return Helpers.GetWeightedAverage(shift_storage, closest_corrections);
            }
        }

        public void AddShift(ShiftPosition cursor_position, Point shift)
        {
            shift_storage.AddShift(cursor_position, shift);
        }

        public void Dispose()
        {
            shift_storage.Dispose();
        }

        public void Reset()
        {
            shift_storage.Reset();
        }

        public void ToggleDebugWindow()
        {
            shift_storage.ToggleDebugWindow();
        }

        private readonly ShiftsStorage shift_storage = new ShiftsStorage();
    }
}