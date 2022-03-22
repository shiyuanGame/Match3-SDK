using System.Collections.Generic;
using System.Linq;
using Implementation.ItemsDrop.Jobs;
using Implementation.ItemsDrop.Models;
using Implementation.ItemsScale.Jobs;
using Match3.Core.Enums;
using Match3.Core.Extensions;
using Match3.Core.Interfaces;
using Match3.Core.Models;
using Match3.Core.Structs;

namespace Implementation.ItemsRollDown
{
    public class ItemsRollDownFillStrategy : IBoardFillStrategy
    {
        private readonly IGameBoard _gameBoard;
        private readonly IItemGenerator _itemGenerator;

        public string Name => "Roll Down Fill Strategy";

        public ItemsRollDownFillStrategy(IGameBoard gameBoard, IItemGenerator itemGenerator)
        {
            _gameBoard = gameBoard;
            _itemGenerator = itemGenerator;
        }

        public IEnumerable<IJob> GetFillJobs()
        {
            return GetFillJobs(0, 0);
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
                var itemsMoveData = GetItemsMoveData(solvedGridSlot.GridPosition.ColumnIndex);
                if (itemsMoveData.Count != 0)
                {
                    jobs.Add(new ItemsMoveJob(itemsMoveData));
                }
            }

            solvedGridSlots.Clear();
            jobs.Add(new ItemsHideJob(itemsToHide));
            jobs.AddRange(GetRollDownJobs(1, 0));
            jobs.AddRange(GetFillJobs(0, 1));

            return jobs;
        }

        private IEnumerable<IJob> GetFillJobs(int delayMultiplier, int executionOrder)
        {
            var jobs = new List<IJob>();

            for (var columnIndex = 0; columnIndex < _gameBoard.ColumnCount; columnIndex++)
            {
                var gridSlot = _gameBoard[0, columnIndex];
                if (gridSlot.State == GridSlotState.NotAvailable)
                {
                    continue;
                }

                jobs.Add(new ItemsDropJob(GetGenerateJobs(columnIndex), delayMultiplier, executionOrder));
            }

            return jobs;
        }

        private IEnumerable<ItemMoveData> GetGenerateJobs(int columnIndex)
        {
            var gridSlot = _gameBoard[0, columnIndex];
            var itemsDropData = new List<ItemMoveData>();

            while (gridSlot.State != GridSlotState.Occupied)
            {
                var item = _itemGenerator.GetItem();
                var itemGeneratorPosition = new GridPosition(-1, columnIndex);
                item.SetWorldPosition(_gameBoard.GetWorldPosition(itemGeneratorPosition));

                var dropPositions = FilterPositions(gridSlot.GridPosition, GetDropPositions(gridSlot));
                if (dropPositions.Count == 0)
                {
                    gridSlot.SetItem(item);
                    itemsDropData.Add(new ItemMoveData(item,
                        new[] { _gameBoard.GetWorldPosition(gridSlot.GridPosition) }));
                    break;
                }

                var destinationGridPosition = dropPositions[dropPositions.Count - 1];
                var destinationGridSlot = _gameBoard[destinationGridPosition];

                destinationGridSlot.SetItem(item);
                itemsDropData.Add(new ItemMoveData(item,
                    dropPositions.Select(gridPosition => _gameBoard.GetWorldPosition(gridPosition)).ToArray()));
            }

            itemsDropData.Reverse();

            return itemsDropData;
        }

        private IEnumerable<IJob> GetRollDownJobs(int delayMultiplier, int executionOrder)
        {
            var jobs = new List<IJob>();

            var leftColumnIndex = 0;
            var rightColumnIndex = _gameBoard.ColumnCount - 1;

            while (leftColumnIndex < rightColumnIndex)
            {
                var itemsMoveData = GetItemsMoveData(leftColumnIndex);
                itemsMoveData.AddRange(GetItemsMoveData(rightColumnIndex));

                leftColumnIndex++;
                rightColumnIndex--;

                if (leftColumnIndex == rightColumnIndex)
                {
                    itemsMoveData.AddRange(GetItemsMoveData(leftColumnIndex));
                }

                if (itemsMoveData.Count != 0)
                {
                    jobs.Add(new ItemsMoveJob(itemsMoveData, delayMultiplier, executionOrder));
                }
            }

            return jobs;
        }

        private List<ItemMoveData> GetItemsMoveData(int columnIndex)
        {
            var itemsMoveData = new List<ItemMoveData>();

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
                itemsMoveData.Add(new ItemMoveData(item,
                    dropPositions.Select(gridPosition => _gameBoard.GetWorldPosition(gridPosition)).ToArray()));
                _gameBoard[dropPositions.Last()].SetItem(item);
            }

            itemsMoveData.Reverse();
            return itemsMoveData;
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

            while (_gameBoard.CanMoveInDirection(gridSlot, GridPosition.Down, out var downGridPosition))
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
            var sideGridSlot = _gameBoard.GetSideGridSlot(gridSlot, direction);
            if (sideGridSlot is { State: GridSlotState.NotAvailable })
            {
                return _gameBoard.CanMoveInDirection(sideGridSlot, GridPosition.Down, out gridPosition);
            }

            gridPosition = GridPosition.Zero;
            return false;
        }

        private List<GridPosition> FilterPositions(GridPosition currentGridPosition, List<GridPosition> gridPositions)
        {
            if (gridPositions.Count == 0 || gridPositions.Count == 1)
            {
                return gridPositions;
            }

            var startColumnIndex = currentGridPosition.ColumnIndex;
            var filteredGridPositions = new HashSet<GridPosition>();

            for (var i = 0; i < gridPositions.Count; i++)
            {
                var gridPosition = gridPositions[i];

                if (startColumnIndex == gridPosition.ColumnIndex)
                {
                    if (i == gridPositions.Count - 1)
                    {
                        filteredGridPositions.Add(gridPosition);
                    }

                    continue;
                }

                if (i > 0)
                {
                    filteredGridPositions.Add(gridPositions[i - 1]);
                }

                filteredGridPositions.Add(gridPosition);

                startColumnIndex = gridPosition.ColumnIndex;
            }

            return filteredGridPositions.ToList();
        }
    }
}