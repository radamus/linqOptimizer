﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections;
using System.Runtime;

namespace OptimizableLINQ
{

    internal class EmptyEnumerable<TElement>
    {
        private static volatile TElement[] instance;

        public static IEnumerable<TElement> Instance
        {
          get
          {
            if (EmptyEnumerable<TElement>.instance == null)
              EmptyEnumerable<TElement>.instance = new TElement[0];
            return (IEnumerable<TElement>) EmptyEnumerable<TElement>.instance;
          }
        }

        [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
        public EmptyEnumerable()
        {
        }
    }

    // Avoids all exceptions occuring in original query concerning indexed predicate
    public class RelaxedVolatileIndex<TKey, TElement>
    {
        // Lookup does not promise to preserve elements order (but currently it does)
        private ILookup<TKey, TElement> validKeysLookup;

        public IEnumerable<TElement> Lookup(Func<TKey> criterion)
        {
            TKey criterionValue;

            try
            {
                criterionValue = criterion();
            }
            catch (Exception e)
            {
                return EmptyEnumerable<TElement>.Instance;
            }

            return validKeysLookup[criterionValue];
        }

        private RelaxedVolatileIndex()
        {
        }

        internal static RelaxedVolatileIndex<TKey, TElement> Create(IEnumerable<TElement> source, Func<TElement, TKey> keySelector)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }

            RelaxedVolatileIndex<TKey, TElement> index = new RelaxedVolatileIndex<TKey, TElement>();

            int elementsCount = 0;
            try
            {
                index.validKeysLookup = source.Where(element =>
                {
                    elementsCount++;
                    try
                    {
                        keySelector(element);
                        return true;
                    }
                    catch (Exception e)
                    {
                        return false;
                    }
                }).ToLookup(keySelector);
            } catch (Exception e) {
                index.validKeysLookup = source.Take(elementsCount).Where(element =>
                {
                    try
                    {
                        keySelector(element);
                        return true;
                    }
                    catch (Exception e2)
                    {
                        return false;
                    }
                }).ToLookup(keySelector);
            }

            return index;
        }
    }
    
    // Used correctly preserves exception thrown occuring in original query
    // LIMITATIONS: Use only for finite data sources.
    public class VolatileIndex<TKey, TElement>
    {
        // Lookup does not promise to preserve elements order (but currently it does)
        private ILookup<TKey, TElement> validKeysLookup;

        private Func<TElement, TKey> keySelector;

        Exception firstOccuringKeyValueException = null;

        private IEnumerable<TElement> source;

        public IEnumerable<TElement> Lookup(Func<TKey> criterion, bool keyOperandBeforeCriterion, bool nonStaticEquals)
        {
            TKey criterionValue;

            try
            {
                criterionValue = criterion(); // exception may occur while evaluation of criterion
                if (nonStaticEquals && !keyOperandBeforeCriterion && criterionValue == null)
                    criterionValue.Equals(null);    // finally exception can be thrown if criterion is null (binding an operator)
            }
            catch (Exception e)
            {
                if (source.Any())
                {
                    TElement firstElement = source.First(); // Firstly exception can be thrown by data source.
                    if (keyOperandBeforeCriterion)
                        keySelector(firstElement); //if keyOperandBeforeCriterion then secondly exception can be thrown by keySelector
                    throw e; // finally exception comes from criterion evaluation or calling equals method on null criterionValue
                }
                return EmptyEnumerable<TElement>.Instance;
            }

            if (firstOccuringKeyValueException != null)
                return GetKeyValueExceptionEndedEnumerable(criterionValue); // if other exceptions are probable they be handled here
                
            return validKeysLookup[criterionValue];
        }

        private IEnumerable<TElement> GetKeyValueExceptionEndedEnumerable(TKey criterionValue) {
            foreach (TElement element in validKeysLookup[criterionValue])
                yield return element;
            throw firstOccuringKeyValueException;
        }

        private VolatileIndex()
        {
        }

        internal static VolatileIndex<TKey, TElement> Create(IEnumerable<TElement> source, Func<TElement, TKey> keySelector, bool nonStaticEqualsOnKeyOperand)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }

            VolatileIndex<TKey, TElement> index = new VolatileIndex<TKey, TElement>();
            index.source = source;
            index.keySelector = keySelector;

            int validElementsCount = 0;

            try
            {
                index.validKeysLookup = source.TakeWhile(element =>
                {
                    try
                    {
                        TKey keyValue = keySelector(element); // firstly exception may occur while evaluation of key operand, 
                        if (nonStaticEqualsOnKeyOperand)
                            keyValue.Equals(null); // secondly (assuming criterion is ok) if it is null 
                        validElementsCount++;
                        return true;
                    }
                    catch (Exception e)
                    {
                        index.firstOccuringKeyValueException = e;
                        return false;
                    }
                }).ToLookup(keySelector);
            } catch (Exception e) {
                index.validKeysLookup = source.Take(validElementsCount).ToLookup(keySelector);
                index.firstOccuringKeyValueException = e;
            }

            return index;
        }
    }


    // Preserves exceptions occuring in original query concerning indexed predicate
    public class PartlyRelaxedVolatileIndex<TKey, TElement>
    {
        // Lookup does not promise to preserve elements order (but currently it does)
        private ILookup<TKey, TElement> validKeysLookup;

        private Func<TElement, TKey> keySelector;

        private IList<KeyValueExceptionElement> keyValueExceptionElements;

        private IEnumerable<TElement> source;

        public IEnumerable<TElement> Lookup(Func<TKey> criterion, bool keyOperandBeforeCriterion, Func<TElement, bool> precedingPredicates = null)
        {
            TKey criterionValue;

            try
            {
                criterionValue = criterion();
            } 
            catch (Exception e)
            {
                if (source.Any()) {
                    if (precedingPredicates == null) {
                        if (keyOperandBeforeCriterion) 
                            keySelector(source.First());
                        throw e;
                    }
                        
                    foreach(TElement unsafeElement in source.Where(element => precedingPredicates(element))) {
                        if (keyOperandBeforeCriterion) 
                            keySelector(unsafeElement);
                        throw e;
                    }
                }
                return EmptyEnumerable<TElement>.Instance;
            }

            if (keyValueExceptionElements.Any())
                if (precedingPredicates == null)
                    throw keyValueExceptionElements.First().e;
                else 
                    foreach (KeyValueExceptionElement kveElement in keyValueExceptionElements.Where(kveElement => precedingPredicates(kveElement.element)))
                        throw kveElement.e;

            if (precedingPredicates == null)
                return validKeysLookup[criterionValue];

            return validKeysLookup[criterionValue].Where(element => precedingPredicates(element));
        }

        private PartlyRelaxedVolatileIndex()
        {
        }

        internal static PartlyRelaxedVolatileIndex<TKey, TElement> Create(IEnumerable<TElement> source, Func<TElement, TKey> keySelector)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }

            PartlyRelaxedVolatileIndex<TKey, TElement> index = new PartlyRelaxedVolatileIndex<TKey, TElement>();
            index.source = source;
            index.keySelector = keySelector;
            index.keyValueExceptionElements = new List<KeyValueExceptionElement>();

            index.validKeysLookup = source.Where(element =>
                {
                    try
                    {
                        keySelector(element);
                        return true;
                    }
                    catch (Exception e)
                    {
                        index.keyValueExceptionElements.Add(new KeyValueExceptionElement { element = element, e = e });
                        return false;
                    }
                }).ToLookup(keySelector);

            return index;
        }
        
        public class KeyValueExceptionElement
        {
            public TElement element;
            internal Exception e = null;
        }
    }

    public class SlowVolatileIndex<TKey, TElement>
    {
        private IndexValuesEnumerable valuesEnumerable;
        
        private IEqualityComparer<TKey> comparer;
                
        private SlowVolatileIndex<TKey, TElement>.Grouping[] groupings;
        private SlowVolatileIndex<TKey, TElement>.Grouping lastGrouping;
        private ArgumentExceptionEnumerable keyExceptionGrouping;
        
        public IEnumerable<VolatileIndexElement> this[Func<TKey> key]
        {
          get
          {
              try
              {
                  SlowVolatileIndex<TKey, TElement>.Grouping grouping = this.GetGrouping(key());
                  if (grouping == null)
                      return EmptyEnumerable<VolatileIndexElement>.Instance;

                  valuesEnumerable.start = grouping.start;
                  valuesEnumerable.stop = grouping.stop;
                  return valuesEnumerable;
              }
              catch (Exception e)
              {
                  keyExceptionGrouping.setKeyException(e);
                  return keyExceptionGrouping;
              }
          }
        }

        private SlowVolatileIndex()
        {
            this.comparer = (IEqualityComparer<TKey>) EqualityComparer<TKey>.Default;
        }

        internal static SlowVolatileIndex<TKey, TElement> Create(IEnumerable<TElement> source, Func<TElement, TKey> keySelector)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }

            SlowVolatileIndex<TKey, TElement> lookup = new SlowVolatileIndex<TKey, TElement>();
            lookup.keyExceptionGrouping = new ArgumentExceptionEnumerable(source);

            lookup.valuesEnumerable = new IndexValuesEnumerable(source, keySelector);

            int distinctKeysCount = 1;
            int sourceWithKeysCount = lookup.valuesEnumerable.orderedSourceWithKeys.Count();
            for (int i = 1; i < sourceWithKeysCount; i++)
                if (!lookup.comparer.Equals(lookup.valuesEnumerable.orderedSourceWithKeys[i].key, lookup.valuesEnumerable.orderedSourceWithKeys[i - 1].key))
                    distinctKeysCount++;

            int countGroupings = 7;
            while (countGroupings < distinctKeysCount)
                countGroupings = checked(2 * countGroupings + 1);
            lookup.groupings = new SlowVolatileIndex<TKey, TElement>.Grouping[countGroupings];

            int k = 0;
            while (k < sourceWithKeysCount)
            {
                TKey key = lookup.valuesEnumerable.orderedSourceWithKeys[k].key;
                SlowVolatileIndex<TKey, TElement>.Grouping grouping = lookup.CreateGrouping(key);
                grouping.start = k;
                while (++k < sourceWithKeysCount && lookup.comparer.Equals(lookup.valuesEnumerable.orderedSourceWithKeys[k].key, key)) ;
                grouping.stop = k;
            }
            return lookup;
        }

        internal int InternalGetHashCode(TKey key)
        {
            if ((object) key != null)
                return this.comparer.GetHashCode(key) & int.MaxValue;
            return 0;
        }

        internal SlowVolatileIndex<TKey, TElement>.Grouping GetGrouping(TKey key)
        {
            int hashCode = this.InternalGetHashCode(key);
            for (SlowVolatileIndex<TKey, TElement>.Grouping grouping = this.groupings[hashCode % this.groupings.Length]; grouping != null; grouping = grouping.hashNext)
            {
                if (grouping.hashCode == hashCode && this.comparer.Equals(grouping.key, key))
                      return grouping;
            }
            return (SlowVolatileIndex<TKey, TElement>.Grouping) null;
        }
        
        internal SlowVolatileIndex<TKey, TElement>.Grouping CreateGrouping(TKey key) {
            int hashCode = this.InternalGetHashCode(key);
            for (SlowVolatileIndex<TKey, TElement>.Grouping grouping = this.groupings[hashCode % this.groupings.Length]; grouping != null; grouping = grouping.hashNext);
            int index = hashCode % this.groupings.Length;
            SlowVolatileIndex<TKey, TElement>.Grouping grouping1 = new SlowVolatileIndex<TKey, TElement>.Grouping();
            grouping1.key = key;
            grouping1.hashCode = hashCode;
            grouping1.hashNext = this.groupings[index];
            this.groupings[index] = grouping1;
            if (this.lastGrouping == null)
            {
                grouping1.next = grouping1;
            }
            else
            {
                grouping1.next = this.lastGrouping.next;
                this.lastGrouping.next = grouping1;
            }
            this.lastGrouping = grouping1;
            return grouping1;
        }

        public class VolatileIndexElement
        {
            public TElement Value;
            internal Exception e = null;
            internal TKey key;

            public bool IsValid
            {
                get
                {
                    if (e == null) return true;
                    else throw e;
                }
            }
        }

        internal class IndexValuesEnumerable : IEnumerable<VolatileIndexElement>
        {
            internal IList<VolatileIndexElement> orderedSourceWithKeys;

            internal IList<VolatileIndexElement> keyValueExceptionElements;

            internal int start, stop;

            internal IndexValuesEnumerable(IEnumerable<TElement> source, Func<TElement, TKey> keySelector)
            {
                this.keyValueExceptionElements = new List<VolatileIndexElement>();

                this.orderedSourceWithKeys = source.Select(element => {
                        try
                        {
                            return new VolatileIndexElement { key = keySelector(element), Value = element };
                        }
                        catch (Exception e)
                        {
                            keyValueExceptionElements.Add(new VolatileIndexElement { Value = element, e = e });
                            return null;
                        }
                    }
                    ).Where(pair => pair != null).OrderBy(pair => pair.key).ToList();
            }

            IEnumerator<VolatileIndexElement> IEnumerable<VolatileIndexElement>.GetEnumerator()
            {
                for (int i = start; i < stop; i++)
                {
                    yield return orderedSourceWithKeys[i];
                }
                foreach (VolatileIndexElement vieWithException in keyValueExceptionElements)
                    yield return vieWithException;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                for (int i = start; i < stop; i++)
                {
                    yield return orderedSourceWithKeys[i];
                }
                foreach (VolatileIndexElement vieWithException in keyValueExceptionElements)
                    yield return vieWithException;
            }
        }

        internal class ArgumentExceptionEnumerable : IEnumerable<VolatileIndexElement>
        {

            private IEnumerable<TElement> source;
            private VolatileIndexElement vie;
            
            internal ArgumentExceptionEnumerable(IEnumerable<TElement> source)
            {
                this.source = source;
                if (source.Any())
                    vie = new VolatileIndexElement { Value = source.First() };
            }

            internal void setKeyException(Exception e) { vie.e = e; }


            IEnumerator<VolatileIndexElement> IEnumerable<VolatileIndexElement>.GetEnumerator()
            {
                foreach(TElement element in source)
                {
                    vie.Value = element;
                    yield return vie;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                foreach (TElement element in source)
                {
                    vie.Value = element;
                    yield return vie;
                }
            }
        }

        internal class Grouping
        {
            internal TKey key;
            internal int hashCode;
            internal int start, stop;
            internal SlowVolatileIndex<TKey, TElement>.Grouping hashNext;
            internal SlowVolatileIndex<TKey, TElement>.Grouping next;

            public TKey Key
            {
                [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")] get
                {
                    return this.key;
                }
            }
            
            [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
            public Grouping()
            {
            }
        }
    }

    public class AlmostVolatileIndex<TKey, TElement>
    {
        private IEqualityComparer<TKey> comparer;
        private AlmostVolatileIndex<TKey, TElement>.Grouping[] groupings;
        private AlmostVolatileIndex<TKey, TElement>.Grouping lastGrouping;
        private AlmostVolatileIndex<TKey, TElement>.Grouping exceptionGrouping;
        private KeyExceptionEnumerable keyExceptionGrouping;
        private int count;

        public int Count
        {
            [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
            get
            {
                return this.count;
            }
        }

        public IEnumerable<VolatileIndexElement> this[Func<TKey> key]
        {
            get
            {
                try
                {
                    return (IEnumerable<VolatileIndexElement>)this.GetGrouping(key(), false) ?? EmptyEnumerable<VolatileIndexElement>.Instance;
                }
                catch (Exception e)
                {
                    keyExceptionGrouping.setKeyException(e);
                    return keyExceptionGrouping;
                }
            }
        }

        private AlmostVolatileIndex(IEqualityComparer<TKey> comparer)
        {
            if (comparer == null)
                comparer = (IEqualityComparer<TKey>)EqualityComparer<TKey>.Default;
            this.comparer = comparer;
            this.groupings = new AlmostVolatileIndex<TKey, TElement>.Grouping[7];
            this.exceptionGrouping = new AlmostVolatileIndex<TKey, TElement>.Grouping();
            this.exceptionGrouping.elements = new VolatileIndexElement[1];
        }

        internal static AlmostVolatileIndex<TKey, TElement> Create<TSource>(IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (keySelector == null)
            {
                throw new ArgumentNullException("keySelector");
            }
            if (elementSelector == null)
            {
                throw new ArgumentNullException("elementSelector");
            }
            AlmostVolatileIndex<TKey, TElement> lookup = new AlmostVolatileIndex<TKey, TElement>(comparer);
            lookup.keyExceptionGrouping = new KeyExceptionEnumerable(source.Select(s => elementSelector(s)));
            foreach (TSource source1 in source)
            {
                TElement element = elementSelector(source1);
                try
                {
                    lookup.GetGrouping(keySelector(source1), true).Add(new VolatileIndexElement(element));
                }
                catch (Exception e)
                {
                    lookup.exceptionGrouping.Add(new VolatileIndexElement(element, e));
                }
            }
            return lookup;
        }

        internal int InternalGetHashCode(TKey key)
        {
            if ((object)key != null)
                return this.comparer.GetHashCode(key) & int.MaxValue;
            return 0;
        }

        internal AlmostVolatileIndex<TKey, TElement>.Grouping GetGrouping(TKey key, bool create)
        {
            int hashCode = this.InternalGetHashCode(key);
            for (AlmostVolatileIndex<TKey, TElement>.Grouping grouping = this.groupings[hashCode % this.groupings.Length]; grouping != null; grouping = grouping.hashNext)
            {
                if (grouping.hashCode == hashCode && this.comparer.Equals(grouping.key, key))
                    return grouping;
            }
            if (!create)
                return (AlmostVolatileIndex<TKey, TElement>.Grouping)null;
            if (this.count == this.groupings.Length)
                this.Resize();
            int index = hashCode % this.groupings.Length;
            AlmostVolatileIndex<TKey, TElement>.Grouping grouping1 = new AlmostVolatileIndex<TKey, TElement>.Grouping();
            grouping1.key = key;
            grouping1.hashCode = hashCode;
            grouping1.elements = new VolatileIndexElement[1];
            grouping1.hashNext = this.groupings[index];
            this.groupings[index] = grouping1;
            if (this.lastGrouping == null)
            {
                grouping1.next = grouping1;
            }
            else
            {
                grouping1.next = this.lastGrouping.next;
                this.lastGrouping.next = grouping1;
            }
            this.lastGrouping = grouping1;
            ++this.count;
            return grouping1;
        }

        private void Resize()
        {
            int length = checked(this.count * 2 + 1);
            AlmostVolatileIndex<TKey, TElement>.Grouping[] groupingArray = new AlmostVolatileIndex<TKey, TElement>.Grouping[length];
            AlmostVolatileIndex<TKey, TElement>.Grouping grouping = this.lastGrouping;
            do
            {
                grouping = grouping.next;
                int index = grouping.hashCode % length;
                grouping.hashNext = groupingArray[index];
                groupingArray[index] = grouping;
            }
            while (grouping != this.lastGrouping);
            this.groupings = groupingArray;
        }

        public class VolatileIndexElement
        {
            public TElement Value;
            internal Exception e = null;
            public bool IsValid
            {
                get
                {
                    if (e == null) return true;
                    else throw e;
                }
            }

            internal VolatileIndexElement(TElement Value, Exception e = null)
            {
                this.Value = Value;
                this.e = e;
            }
        }

        internal class KeyExceptionEnumerable : IEnumerable<VolatileIndexElement>
        {

            private IEnumerable<TElement> source;
            private VolatileIndexElement vie;

            public KeyExceptionEnumerable(IEnumerable<TElement> source)
            {
                this.source = source;
                vie = new VolatileIndexElement(source.GetEnumerator().Current);
            }

            internal void setKeyException(Exception e) { vie.e = e; }


            IEnumerator<VolatileIndexElement> IEnumerable<VolatileIndexElement>.GetEnumerator()
            {
                foreach (TElement element in source)
                {
                    vie.Value = element;
                    yield return vie;
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                foreach (TElement element in source)
                {
                    vie.Value = element;
                    yield return vie;
                }
            }
        }

        internal class Grouping : IGrouping<TKey, VolatileIndexElement>, IList<VolatileIndexElement>, ICollection<VolatileIndexElement>, IEnumerable<VolatileIndexElement>, IEnumerable
        {
            internal TKey key;
            internal int hashCode;
            internal VolatileIndexElement[] elements;
            internal int count;
            internal AlmostVolatileIndex<TKey, TElement>.Grouping hashNext;
            internal AlmostVolatileIndex<TKey, TElement>.Grouping next;

            public TKey Key
            {
                [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
                get
                {
                    return this.key;
                }
            }

            int ICollection<VolatileIndexElement>.Count
            {
                get
                {
                    return this.count;
                }
            }

            bool ICollection<VolatileIndexElement>.IsReadOnly
            {
                get
                {
                    return true;
                }
            }

            VolatileIndexElement IList<VolatileIndexElement>.this[int index]
            {
                get
                {
                    if (index < 0 || index >= this.count)
                        throw new ArgumentOutOfRangeException("index");
                    return this.elements[index];
                }
                set
                {
                    throw new NotSupportedException();
                }
            }

            [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
            public Grouping()
            {
            }

            internal void Add(VolatileIndexElement element)
            {
                if (this.elements.Length == this.count)
                    Array.Resize<VolatileIndexElement>(ref this.elements, checked(this.count * 2));
                this.elements[this.count] = element;
                ++this.count;
            }

            public IEnumerator<VolatileIndexElement> GetEnumerator()
            {
                for (int i = 0; i < this.count; ++i)
                    yield return this.elements[i];
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return (IEnumerator)this.GetEnumerator();
            }

            void ICollection<VolatileIndexElement>.Add(VolatileIndexElement item)
            {
                throw new NotSupportedException();
            }

            void ICollection<VolatileIndexElement>.Clear()
            {
                throw new NotSupportedException();
            }

            bool ICollection<VolatileIndexElement>.Contains(VolatileIndexElement item)
            {
                return Array.IndexOf<VolatileIndexElement>(this.elements, item, 0, this.count) >= 0;
            }

            void ICollection<VolatileIndexElement>.CopyTo(VolatileIndexElement[] array, int arrayIndex)
            {
                Array.Copy((Array)this.elements, 0, (Array)array, arrayIndex, this.count);
            }

            bool ICollection<VolatileIndexElement>.Remove(VolatileIndexElement item)
            {
                throw new NotSupportedException();
            }

            int IList<VolatileIndexElement>.IndexOf(VolatileIndexElement item)
            {
                return Array.IndexOf<VolatileIndexElement>(this.elements, item, 0, this.count);
            }

            void IList<VolatileIndexElement>.Insert(int index, VolatileIndexElement item)
            {
                throw new NotSupportedException();
            }

            void IList<VolatileIndexElement>.RemoveAt(int index)
            {
                throw new NotSupportedException();
            }
        }
    }

}

