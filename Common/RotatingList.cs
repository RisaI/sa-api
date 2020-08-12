using System;
using System.Collections;
using System.Collections.Generic;
using static SAApi.Helper;

namespace SAApi.Common
{
    public class RotatingList<T>
    {
        public int Capacity { get { return _Objects.Length; } }

        private int _Cursor = 0;
        private T[] _Objects;
        
        public RotatingList(int capacity)
        {
            _Objects = new T[capacity];
        }

        public void Push(T obj)
        {
            _Objects[_Cursor] = obj;
            _Cursor = Mod(_Cursor + 1, Capacity);
        }

        public T this[int index]
        {
            get { return _Objects[Mod(_Cursor + index, Capacity)]; }
        }
    }
}