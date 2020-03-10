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
    class CalibrationManagerV1 : ICalibrationManager
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


                ApplyShades(closest_corrections);

                foreach (var correction in closest_corrections)
                {
                    correction.weight = correction.weight / correction.distance / sum_of_reverse_distances;
                }

                Helpers.NormalizeWeights(closest_corrections);

                if (shift_storage.calibration_window != null)
                {
                    var lables = new List<Tuple<string /*text*/, int /*correction index*/>>();
                    foreach(var correction in closest_corrections)
                        lables.Add(new Tuple<string, int>((int)(correction.weight * 100) + "%", correction.index));
                    shift_storage.calibration_window.UpdateCorrectionsLables(lables);
                }

                return Helpers.GetWeightedAverage(shift_storage, closest_corrections);
            }
        }

        private double GetShadeOpacity(
            Helpers.CorrectionInfoRelatedToCursor source_of_shade, 
            Helpers.CorrectionInfoRelatedToCursor shaded_correction)
        {
            Debug.Assert(source_of_shade.distance < shaded_correction.distance);

            var mode = Options.Instance.calibration_mode;

            double opacity = 1;

            double angle_in_percents = Helpers.GetAngleBetweenVectors(source_of_shade, shaded_correction) * 100 / Math.PI;
            Debug.Assert(angle_in_percents <= 100);

            // Opacity descendes gradualy in the sector between opaque and transparent sectors.
            if (angle_in_percents < mode.size_of_opaque_sector_in_percents)
                opacity = 1;
            else if (angle_in_percents > 100 - mode.size_of_transparent_sector_in_percents)
                opacity = 0;
            else
                opacity = (angle_in_percents + mode.size_of_transparent_sector_in_percents - 100) / 
                    (mode.size_of_opaque_sector_in_percents + mode.size_of_transparent_sector_in_percents - 100);

            double distance_from_shade_shell_to_shaded_correction = shaded_correction.distance - source_of_shade.distance;
            if (distance_from_shade_shell_to_shaded_correction < mode.shade_thickness_in_pixels)
                opacity *= distance_from_shade_shell_to_shaded_correction / mode.shade_thickness_in_pixels;

            return opacity * source_of_shade.weight;
        }

        private void ApplyShades(List<Helpers.CorrectionInfoRelatedToCursor> corrections)
        {
            for (int i = 0; i < corrections.Count;)
            {
                corrections[i].weight = 1;

                for (int j = 0; j < i; j++)
                {
                    corrections[i].weight -= GetShadeOpacity(corrections[j], corrections[i]);
                }
                if (corrections[i].weight <= 0.01)
                    corrections.RemoveAt(i);
                else
                    i++;
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