﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using System.Threading;

namespace eye_tracking_mouse
{

    public class ShiftPosition
    {
        public ShiftPosition(List<double> coordinates)
        {
            this.coordinates = coordinates;
        }

        public List<double> coordinates;

        [JsonIgnore]
        public double X
        {
            get { return coordinates[0]; }
        }

        [JsonIgnore]
        public double Y
        {
            get { return coordinates[1]; }
        }

        // To calculate points density we split the screen to sectors. This algprithm is not accurate but simple and fast
        [JsonIgnore]
        public int SectorX
        {
            get
            {
                return (int)(X / 500.0);
            }
        }

        [JsonIgnore]
        public int SectorY
        {
            get
            {
                return (int)(Y / 500.0);
            }
        }

        public double GetDistance(ShiftPosition other)
        {
            double squared_distance = 0;

            for (int i = 0; i < coordinates.Count; i++)
            {
                double factor = i < 2 ? 1 : Math.Pow(Options.Instance.calibration_mode.multi_dimensions_detalization / 10.0, 2);
                squared_distance += Math.Pow(coordinates[i] - other.coordinates[i], 2) * factor;
            }

            return Math.Pow(squared_distance, 0.5);
        }
    }

    interface ICalibrationManager :  IDisposable
    {
        void ToggleDebugWindow();
        void Reset();
        void AddShift(ShiftPosition cursor_position, Point shift);
        Point GetShift(ShiftPosition cursor_position);
    }

    static class CalibrationManager
    {
        static public ICalibrationManager Instance { 
            get
            {
                if (instance == null)
                {

                    Settings.CalibrationModeChanged += ReloadInstance;
                    ReloadInstance(null, null);
                }
                return instance;
            }
        }

        static private ICalibrationManager instance;

        static private void ReloadInstance(object sender, EventArgs args)
        {
            instance?.Dispose();
            instance = new ShiftsStorage();
        }
    }

    public class ShiftItem
    {
        public ShiftItem(ShiftPosition position, Point shift)
        {
            Position = position;
            Shift = shift;
        }

        public Point Shift { get; private set; }

        public ShiftPosition Position { get; private set; }
    }

    class ShiftsStorage : ICalibrationManager
    {
        private static CalibrationWindow calibration_window = null;

        public ShiftsStorage()
        {
            Settings.OptionsChanged += OnSettingsChanged;
            LoadFromFile();
        }


        public void Dispose()
        {
            Settings.OptionsChanged -= OnSettingsChanged;
            FilesSavingQueue.FlushSynchroniously();
            if (calibration_window != null)
            {
                calibration_window.Close();
                calibration_window = null;
            }
        }


        public void ToggleDebugWindow()
        {
            lock(Helpers.locker)
            {
                if (calibration_window == null)
                {
                    calibration_window = new CalibrationWindow();
                    calibration_window.Show();
                    calibration_window.UpdateShifts(Shifts);
                }
                else
                {
                    calibration_window.Close();
                    calibration_window = null;
                }
            }
        }

        private static string GetVector3PathPart(Vector3Bool vector)
        {
            return (vector.X ? "1" : "0") + (vector.Y ? "1" : "0") + (vector.Z ? "1" : "0");
        }

        private static string Filepath
        {
            get
            {
                var dimensions_config = Options.Instance.calibration_mode.additional_dimensions_configuration;

                return Path.Combine(Helpers.UserDataFolder, "calibration" +
                  GetVector3PathPart(dimensions_config.LeftEye) +
                  GetVector3PathPart(dimensions_config.RightEye) +
                  GetVector3PathPart(dimensions_config.HeadPosition) +
                  GetVector3PathPart(dimensions_config.HeadDirection) +
                  ".json");
            }
        }

        public List<ShiftItem> Shifts { private set; get; } = new List<ShiftItem>();

        private void LoadFromFile()
        {
            try
            {
                Shifts.Clear();
                if (!File.Exists(Filepath))
                    return;

                bool error_message_box_shown = false;

                Shifts = JsonConvert.DeserializeObject<List<ShiftItem>>(File.ReadAllText(Filepath)).Where(x=> {
                    if (x.Position.coordinates == null)
                        return false;
                    if (x.Position.coordinates.Count != Options.Instance.calibration_mode.additional_dimensions_configuration.CoordinatesCount)
                    {
                        if (!error_message_box_shown)
                        {
                            error_message_box_shown = true;
                            System.Windows.MessageBox.Show("Number of coordinates in the file doesn't fit options.");
                        }
                        return false;
                    }
                    return true; 
                }).ToList();
                
            }
            catch (Exception e)
            {
                Shifts = new List<ShiftItem>();
                System.Windows.MessageBox.Show("Failed reading shifts storage: " + e.Message, Helpers.application_name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public Point GetShift(ShiftPosition cursor_position)
        {
            lock(Helpers.locker)
            {
                calibration_window?.OnCursorPositionUpdate(cursor_position);

                var closest_indices = GetClosestShiftIndexes(cursor_position, Options.Instance.calibration_mode.considered_zones_count);
                if (closest_indices == null)
                {
                    Debug.Assert(Shifts.Count() == 0);
                    return new Point(0, 0);
                }

                double sum_of_reverse_distances = 0;
                foreach (var index in closest_indices)
                {
                    sum_of_reverse_distances += (1 / index.Item2);
                }

                Point resulting_shift = new Point(0, 0);
                foreach (var index in closest_indices)
                {
                    resulting_shift.X += (int)(Shifts[index.Item1].Shift.X / index.Item2 / sum_of_reverse_distances);
                    resulting_shift.Y += (int)(Shifts[index.Item1].Shift.Y / index.Item2 / sum_of_reverse_distances);
                }

                return resulting_shift;
            }
        }

        public void Reset()
        {
            lock (Helpers.locker)
            {
                Shifts.Clear();
                OnShiftsChanged();
            }
        }

        private void OnSettingsChanged(object sender, EventArgs e)
        {
            // Adjust to new calibration zone size.
            for (int i = 0; i < Shifts.Count - 1;)
            {
                bool did_remove = false;
                for (int j = i + 1; j < Shifts.Count; j++)
                {
                    if (Shifts[j].Position.GetDistance(Shifts[i].Position) < Options.Instance.calibration_mode.zone_size)
                    {
                        did_remove = true;
                        Shifts.RemoveAt(i);
                        break;
                    }
                }

                if (!did_remove)
                    i++;
            }

            // Adjust to new calibration zones count.
            while (Shifts.Count > Options.Instance.calibration_mode.max_zones_count)
                Shifts.RemoveAt(0);

            OnShiftsChanged();
        }

        private string GetDeepCopy()
        {
            lock (Helpers.locker)
            {
                return JsonConvert.SerializeObject(Shifts);
            }
        }

        public void AddShift(ShiftPosition cursor_position, Point shift)
        {
            var indices = GetClosestShiftIndexes(cursor_position, 2);
            if (indices != null && indices[0].Item2 < Options.Instance.calibration_mode.zone_size)
            {
                Shifts[indices[0].Item1] = new ShiftItem(cursor_position, shift);
                if (indices.Count > 1 && indices[1].Item2 < Options.Instance.calibration_mode.zone_size)
                    Shifts.RemoveAt(indices[1].Item1);
            }
            else if (Shifts.Count() < Options.Instance.calibration_mode.max_zones_count)
            {
                Shifts.Add(new ShiftItem(cursor_position, shift));
            }
            else
            {
                Shifts[GetClosestPointOfHihestDensity(cursor_position)] = new ShiftItem(cursor_position, shift);
            }

            OnShiftsChanged();
        }

        private void OnShiftsChanged()
        {
            lock (Helpers.locker)
            {
                FilesSavingQueue.Save(Filepath, GetDeepCopy);
                calibration_window?.UpdateShifts(Shifts);
            }
        }

        private int GetSectorNumber(ShiftItem shift, int max_sector_x)
        {
            return shift.Position.SectorX + shift.Position.SectorY * (max_sector_x + 1);
        }

        private int GetClosestPointOfHihestDensity(ShiftPosition cursor_position)
        {
            Debug.Assert(Shifts.Count > 0);

            int max_sector_x = 0;
            for (int i = 0; i < Shifts.Count; i++)
                if (Shifts[i].Position.SectorX > max_sector_x)
                    max_sector_x = Shifts[i].Position.SectorX;

            var sectors = new Dictionary<int /*number of sector*/, int /*Count of points in sector*/>();
            for (int i = 0; i < Shifts.Count; i++)
            {
                int sector_number = GetSectorNumber(Shifts[i], max_sector_x);
                if (!sectors.ContainsKey(sector_number))
                    sectors.Add(sector_number, 0);

                sectors[sector_number]++;
            }

            int max_points_count_in_sector = 0;
            for (int i = 0; i < sectors.Count; i++)
            {
                int points_number_in_sector = sectors.ElementAt(i).Value;
                if (points_number_in_sector > max_points_count_in_sector)
                    max_points_count_in_sector = points_number_in_sector;
            }

            int index_of_closest_point = 0;
            double min_distance = double.MaxValue;
            for (int i = 0; i < Shifts.Count; i++)
            {
                if (sectors[GetSectorNumber(Shifts[i], max_sector_x)] == max_points_count_in_sector)
                {
                    double distance = Shifts[i].Position.GetDistance(cursor_position);
                    if (min_distance > distance)
                    {
                        min_distance = distance;
                        index_of_closest_point = i;
                    }
                }
            }

            return index_of_closest_point;
        }

        private List<Tuple<int /*index*/, double /*distance*/>> GetClosestShiftIndexes(ShiftPosition cursor_position, int number)
        {
            if (Shifts.Count() == 0)
                return null;

            var retval = new List<Tuple<int, double>>();
            for (int i = 0; i < Shifts.Count(); i++)
            {
                double distance = Shifts[i].Position.GetDistance(cursor_position);
                if (distance < 0.1)
                    distance = 0.1;

                if (retval.Count == 0)
                {
                    retval.Add(new Tuple<int, double>(i, distance));
                    continue;
                }

                int j = 0;
                for (; j < retval.Count; j++)
                {
                    if (distance < retval[j].Item2)
                    {
                        retval.Insert(j, new Tuple<int, double>(i, distance));
                        break;
                    }
                }
                if (j == retval.Count)
                    retval.Add(new Tuple<int, double>(i, distance));

                if (retval.Count > number)
                    retval.RemoveAt(retval.Count - 1);
            }
            return retval;
        }
    }
}
