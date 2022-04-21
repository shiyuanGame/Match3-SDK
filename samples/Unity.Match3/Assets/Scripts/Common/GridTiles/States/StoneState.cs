using Common.Enums;

namespace Common.GridTiles.States
{
    public class StoneState : StatefulGridTile
    {
        private bool _isLocked = true;
        private bool _canContainItem;
        private TileGroup _group = TileGroup.Stone;

        public override int GroupId => (int) _group;
        public override bool IsLocked => _isLocked;
        public override bool CanContainItem => _canContainItem;

        protected override void OnComplete()
        {
            _isLocked = false;
            _canContainItem = true;
            _group = TileGroup.Available;
        }

        protected override void OnReset()
        {
            _isLocked = true;
            _canContainItem = false;
            _group = TileGroup.Stone;
        }
    }
}