using System.Collections.Generic;
using System.Linq;
using Implementation.ItemsDrop.Jobs;
using Implementation.ItemsDrop.Models;
using Implementation.ItemsScale.Jobs;
using Match3.Core.Enums;
using Match3.Core.Interfaces;
using Match3.Core.Models;
using Match3.Core.Structs;
using UnityEngine;

namespace Implementation.ItemsRollDown
{
    public class ItemsRollDownFillStrategy : IBoardFillStrategy
    {
        private readonly IGameBoard _gameBoard;
        private readonly IItemGenerator _itemGenerator;
        private readonly Dictionary<int, int> _gridSlotColumnIndexes;

        public string Name => "Roll Down Fill Strategy";

        public ItemsRollDownFillStrategy(IGameBoard gameBoard, IItemGenerator itemGenerator)
        {
            _gameBoard = gameBoard;
            _itemGenerator = itemGenerator;
            _gridSlotColumnIndexes = new Dictionary<int, int>();
        }

        public IEnumerable<IJob> GetFillJobs()
        {
            return GetFillJobs(0);
        }

        public IEnumerable<IJob> GetSolveJobs(IEnumerable<ItemSequence> sequences)
        {
            var jobs = new List<IJob>();
            var itemsToHide = new List<IItem>();
            var solvedGridSlots = new HashSet<GridSlot>();

            foreach (var sequence in sequences)
            {
                foreach (var solvedGridSlot in sequence.SolvedGridSlots)
                {
                    if (solvedGridSlots.Add(solvedGridSlot) == false)
                    {
                        continue;
                    }

                    var item = solvedGridSlot.Item;
                    itemsToHide.Add(item);
                    solvedGridSlot.Clear();

                    _itemGenerator.ReturnItem(item);
                }
            }

            foreach (var solvedGridSlot in solvedGridSlots.OrderBy(slot => CanDropFromTop(slot.GridPosition)))
            {
                if (solvedGridSlot.State == GridSlotState.Occupied)
                {
                    continue;
                }

                var columnIndex = solvedGridSlot.GridPosition.ColumnIndex;
                var itemsMoveData = GetItemsMoveData(columnIndex);

                if (CanDropFromTop(solvedGridSlot.GridPosition) == false)
                {
                    var generatorColumnIndex = GetGridSlotColumnIndex(solvedGridSlot);
                    var indexer = generatorColumnIndex > columnIndex ? 1 : -1;

                    do
                    {
                        columnIndex += indexer;
                        itemsMoveData.InsertRange(0, GetItemsMoveData(columnIndex));
                    } while (columnIndex != generatorColumnIndex);
                }

                if (itemsMoveData.Count != 0)
                {
                    jobs.Add(new ItemsMoveJob(itemsMoveData));
                }

                jobs.AddRange(GetGenerateJobs(columnIndex, 1));
            }

            solvedGridSlots.Clear();
            jobs.Add(new ItemsHideJob(itemsToHide));

            return jobs;
        }

        private IEnumerable<IJob> GetFillJobs(int delayMultiplier)
        {
            var jobs = new List<IJob>();

            for (var columnIndex = 0; columnIndex < _gameBoard.ColumnCount; columnIndex++)
            {
                var gridSlot = _gameBoard[0, columnIndex];
                if (gridSlot.State == GridSlotState.NotAvailable)
                {
                    continue;
                }

                jobs.AddRange(GetGenerateJobs(columnIndex, delayMultiplier));
            }

            return jobs;
        }

        private IEnumerable<IJob> GetGenerateJobs(int columnIndex, int delayMultiplier)
        {
            var jobs = new List<IJob>();
            var gridSlot = _gameBoard[0, columnIndex];
            var itemsDropData = new List<ItemMoveData>();

            while (gridSlot.State != GridSlotState.Occupied)
            {
                var item = _itemGenerator.GetItem();
                var itemGeneratorPosition = new GridPosition(-1, columnIndex);
                item.SetWorldPosition(_gameBoard.GetWorldPosition(itemGeneratorPosition));

                var positionsList = new List<Vector3>();
                var itemDropData = new ItemMoveData(item, positionsList);

                var dropPositions = FilterPositions(gridSlot.GridPosition, GetDropPositions(gridSlot));
                if (dropPositions.Count == 0)
                {
                    gridSlot.SetItem(item);
                    positionsList.Add(_gameBoard.GetWorldPosition(gridSlot.GridPosition));
                    itemsDropData.Add(itemDropData);
                    SetGridSlotColumnIndex(gridSlot, columnIndex);
                    break;
                }

                var destinationGridPosition = dropPositions[dropPositions.Count - 1];
                var destinationGridSlot = _gameBoard[destinationGridPosition];

                positionsList.AddRange(dropPositions.Select(gridPosition =>
                    _gameBoard.GetWorldPosition(gridPosition)));

                destinationGridSlot.SetItem(item);
                itemsDropData.Add(itemDropData);
                SetGridSlotColumnIndex(destinationGridSlot, columnIndex);
            }

            if (itemsDropData.Count > 0)
            {
                itemsDropData.Reverse();
                jobs.Add(new ItemsDropJob(itemsDropData, delayMultiplier));
            }

            return jobs;
        }

        private List<ItemMoveData> GetItemsMoveData(int columnIndex)
        {
            var itemsDropData = new List<ItemMoveData>();

            for (var rowIndex = _gameBoard.RowCount - 1; rowIndex >= 0; rowIndex--)
            {
                var gridSlot = _gameBoard[rowIndex, columnIndex];
                if (gridSlot.State != GridSlotState.Occupied)
                {
                    continue;
                }

                var dropPositions = FilterPositions(gridSlot.GridPosition, GetDropPositions(gridSlot));
                if (dropPositions.Count == 0)
                {
                    continue;
                }

                var item = gridSlot.Item;
                gridSlot.Clear();
                itemsDropData.Add(new ItemMoveData(item,
                    dropPositions.Select(gridPosition => _gameBoard.GetWorldPosition(gridPosition))));
                _gameBoard[dropPositions.Last()].SetItem(item);
            }

            itemsDropData.Reverse();
            return itemsDropData;
        }

        private void SetGridSlotColumnIndex(GridSlot gridSlot, int columnIndex)
        {
            var gridSlotIndex = GetGridSlotIndex(gridSlot.GridPosition);

            if (_gridSlotColumnIndexes.ContainsKey(gridSlotIndex) == false)
            {
                _gridSlotColumnIndexes.Add(gridSlotIndex, columnIndex);
            }
        }

        private int GetGridSlotColumnIndex(GridSlot gridSlot)
        {
            return _gridSlotColumnIndexes[GetGridSlotIndex(gridSlot.GridPosition)];
        }

        private bool CanDropFromTop(GridPosition gridPosition)
        {
            return CanDropFromTop(gridPosition.RowIndex, gridPosition.ColumnIndex);
        }

        private bool CanDropFromTop(int rowIndex, int columnIndex)
        {
            while (rowIndex >= 0)
            {
                if (_gameBoard[rowIndex, columnIndex].State == GridSlotState.NotAvailable)
                {
                    return false;
                }

                rowIndex--;
            }

            return true;
        }

        private List<GridPosition> GetDropPositions(GridSlot gridSlot)
        {
            var dropGridPositions = new List<GridPosition>();

            while (CanMoveInDirection(gridSlot, GridPosition.Down, out var downGridPosition))
            {
                gridSlot = _gameBoard[downGridPosition];
                dropGridPositions.Add(downGridPosition);
            }

            if (CanDropDiagonally(gridSlot, out var diagonalGridPosition) == false)
            {
                return dropGridPositions;
            }

            dropGridPositions.Add(diagonalGridPosition);
            dropGridPositions.AddRange(GetDropPositions(_gameBoard[diagonalGridPosition]));

            return dropGridPositions;
        }

        private bool CanDropDiagonally(GridSlot gridSlot, out GridPosition gridPosition)
        {
            return CanDropDiagonally(gridSlot, GridPosition.Left, out gridPosition) ||
                   CanDropDiagonally(gridSlot, GridPosition.Right, out gridPosition);
        }

        private bool CanDropDiagonally(GridSlot gridSlot, GridPosition direction, out GridPosition gridPosition)
        {
            var sideGridSlot = GetSideGridSlot(gridSlot, direction);
            if (sideGridSlot is {State: GridSlotState.NotAvailable})
            {
                return CanMoveInDirection(sideGridSlot, GridPosition.Down, out gridPosition);
            }

            gridPosition = GridPosition.Zero;
            return false;
        }

        private bool CanMoveInDirection(GridSlot gridSlot, GridPosition direction, out GridPosition gridPosition)
        {
            var downGridSlot = GetSideGridSlot(gridSlot, direction);
            if (downGridSlot == null)
            {
                gridPosition = GridPosition.Zero;
                return false;
            }

            if (downGridSlot.State == GridSlotState.Free || downGridSlot.State == GridSlotState.Solved)
            {
                gridPosition = downGridSlot.GridPosition;
                return true;
            }

            gridPosition = GridPosition.Zero;
            return false;
        }

        private GridSlot GetSideGridSlot(GridSlot gridSlot, GridPosition direction)
        {
            var sideGridSlotPosition = gridSlot.GridPosition + direction;

            return _gameBoard.IsPositionOnGrid(sideGridSlotPosition)
                ? _gameBoard[sideGridSlotPosition]
                : null;
        }

        private List<GridPosition> FilterPositions(GridPosition currentGridPosition, List<GridPosition> gridPositions)
        {
            if (gridPositions.Count == 0 || gridPositions.Count == 1)
            {
                return gridPositions;
            }

            var dictionary = new Dictionary<int, List<GridPosition>>();
            foreach (var gridPosition in gridPositions)
            {
                if (dictionary.ContainsKey(gridPosition.ColumnIndex))
                {
                    dictionary[gridPosition.ColumnIndex].Add(gridPosition);
                }
                else
                {
                    dictionary.Add(gridPosition.ColumnIndex, new List<GridPosition> {gridPosition});
                }
            }

            var groupIndex = -1;
            var filteredGridPositions = new List<GridPosition>();

            foreach (var columnGridPositions in dictionary.Values)
            {
                groupIndex++;

                var count = columnGridPositions.Count;
                if (count == 1)
                {
                    filteredGridPositions.Add(columnGridPositions[0]);
                    continue;
                }

                var gridPosition = columnGridPositions[0];
                if (CanDropFromTop(gridPosition))
                {
                    filteredGridPositions.Add(columnGridPositions[count - 1]);
                    continue;
                }

                if (groupIndex == 0 && currentGridPosition.ColumnIndex == gridPosition.ColumnIndex)
                {
                    filteredGridPositions.Add(columnGridPositions[count - 1]);
                    continue;
                }

                filteredGridPositions.Add(gridPosition);
                filteredGridPositions.Add(columnGridPositions[count - 1]);
            }

            return filteredGridPositions;
        }

        private int GetGridSlotIndex(GridPosition gridPosition)
        {
            return gridPosition.RowIndex * _gameBoard.ColumnCount + gridPosition.ColumnIndex;
        }
    }
}