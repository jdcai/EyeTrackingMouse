﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

namespace eye_tracking_mouse
{
    // Tracks history of gaze points deleting those which are too far from the last point.
    // Longer the user stares at one area smoother resulting gaze point.
    static class CoordinateSmoother
    {
        private static readonly List<EyeTrackerErrorCorrection> points = new List<EyeTrackerErrorCorrection>();

        public static EyeTrackerErrorCorrection Smoothen(EyeTrackerErrorCorrection correction)
        {
            AddPoint(correction);
            float[] coordinates = new float[correction.сoordinates.Length];
            Point shift = new Point(0, 0);

            foreach(var point in points)
            {
                shift.X += point.shift.X;
                shift.Y += point.shift.Y;
                for (int i = 0; i < coordinates.Length; i++)
                {
                    coordinates[i] += point.сoordinates[i];
                }
            }

            shift.X = shift.X / points.Count;
            shift.Y = shift.Y / points.Count;
            for (int i = 0; i < coordinates.Length; i++)
            {

                coordinates[i] = coordinates[i]/ points.Count;
            }
            return new EyeTrackerErrorCorrection(coordinates, shift);
        }

        public static void Reset()
        {
            points.Clear();
        }

        private static void AddPoint(EyeTrackerErrorCorrection correction)
        {
            lock(Helpers.locker)
            {
                points.Insert(0, correction);
                while (points.Count > Options.Instance.smothening_points_count)
                    points.RemoveAt(points.Count - 1);

                points.RemoveAll(p =>
                {
                    if (p.сoordinates.Length != correction.сoordinates.Length)
                        return true;
                    for (int i = 0; i < p.сoordinates.Length; i++)
                    {
                        if (Math.Abs(p.сoordinates[i] - correction.сoordinates[i]) > Options.Instance.instant_jump_distance)
                        {
                            return true;
                        }
                    }
                    return false;
                });
            }
        }
    }
}
