// Q14. React component that renders 1,000,000 products efficiently.
// Covers: virtualization, infinite scrolling, debounced search,
// API request cancellation, memoization, error handling, and retry logic.
//
// Dependencies (npm install): react-window, react-window-infinite-loader
//
// CSS file (add to styles or CSS-in-JS):
// .product-list-container { max-width: 1200px; margin: 0 auto; }
// .search-box { padding: 20px; background: #f5f5f5; border-bottom: 1px solid #ddd; }
// .search-box input { width: 100%; padding: 10px; font-size: 16px; }
// .product-row { display: flex; justify-content: space-between; align-items: center; 
//                padding: 12px 20px; border-bottom: 1px solid #eee; }
// .product-row-skeleton { background: linear-gradient(90deg, #f0f0f0 25%, #e0e0e0 50%, #f0f0f0 75%); 
//                         background-size: 200% 100%; animation: loading 1.5s infinite; }
// @keyframes loading { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
// .skeleton { height: 16px; border-radius: 4px; background: #ddd; }
// .error-message { background: #fee; color: #c33; padding: 15px 20px; border-radius: 4px; margin: 10px; }
// .empty-state, .loading-state { text-align: center; padding: 60px 20px; color: #666; }
// .spinner { border: 3px solid #f3f3f3; border-top: 3px solid #3498db; border-radius: 50%; 
//            width: 40px; height: 40px; animation: spin 1s linear infinite; margin: 20px auto; }
// @keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
// .product-count { text-align: center; padding: 15px; color: #999; font-size: 14px; }

import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { FixedSizeList as List } from 'react-window';
import InfiniteLoader from 'react-window-infinite-loader';

const PAGE_SIZE = 50;
const ITEM_HEIGHT = 72;

// Debounce hook: only commits the latest value after the user pauses typing,
// so we don't fire an API request on every keystroke.
function useDebouncedValue(value, delayMs) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}

export default function ProductList() {
  const [searchTerm, setSearchTerm] = useState('');
  const debouncedSearch = useDebouncedValue(searchTerm, 350);

  const [items, setItems] = useState([]); // sparse array indexed by absolute position
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null); // track fetch errors
  const [retryCount, setRetryCount] = useState({}); // per-page retry count

  const abortControllerRef = useRef(null);
  const loadedPagesRef = useRef(new Set());
  const failedPagesRef = useRef(new Set()); // track pages that failed

  const fetchPage = useCallback(
    async (page, retryAttempt = 0) => {
      if (loadedPagesRef.current.has(page) && retryAttempt === 0) return;
      
      // Cancel any in-flight request before starting a new one -- prevents a
      // slow earlier response from overwriting a newer search's results.
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      setLoading(true);
      setError(null);

      try {
        const params = new URLSearchParams({
          keyword: debouncedSearch,
          page: String(page),
          pageSize: String(PAGE_SIZE),
        });

        const response = await fetch(`/api/products?${params}`, { 
          signal: controller.signal,
          headers: { 'Accept': 'application/json' }
        });

        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`);
        }

        const data = await response.json();

        // Validate response structure
        if (!data.items || !Array.isArray(data.items)) {
          throw new Error('Invalid response format: missing or invalid items array');
        }

        loadedPagesRef.current.add(page);
        failedPagesRef.current.delete(page);

        setItems((prev) => {
          const next = [...prev];
          data.items.forEach((item, i) => {
            next[(page - 1) * PAGE_SIZE + i] = item;
          });
          return next;
        });
        setTotalCount(data.totalCount || 0);
      } catch (err) {
        if (err.name === 'AbortError') {
          return; // Request was cancelled, don't treat as error
        }

        console.error(`Failed to fetch page ${page}:`, err);
        failedPagesRef.current.add(page);
        
        // Retry logic: retry up to 3 times with exponential backoff
        if (retryAttempt < 3) {
          const delay = Math.min(1000 * Math.pow(2, retryAttempt), 10000);
          console.log(`Retrying page ${page} after ${delay}ms (attempt ${retryAttempt + 1}/3)`);
          
          setTimeout(() => {
            fetchPage(page, retryAttempt + 1);
          }, delay);
        } else {
          setError(`Failed to load products. Please try again. (Page ${page})`);
        }
      } finally {
        setLoading(false);
      }
    },
    [debouncedSearch]
  );

  // Reset and reload page 1 whenever the debounced search term changes.
  useEffect(() => {
    setItems([]);
    setTotalCount(0);
    setError(null);
    loadedPagesRef.current.clear();
    failedPagesRef.current.clear();
    fetchPage(1);
    return () => abortControllerRef.current?.abort();
  }, [debouncedSearch, fetchPage]);

  const isItemLoaded = useCallback((index) => !!items[index], [items]);

  const loadMoreItems = useCallback(
    (startIndex) => fetchPage(Math.floor(startIndex / PAGE_SIZE) + 1),
    [fetchPage]
  );

  // React.memo on the row renderer avoids re-rendering rows whose data hasn't
  // changed when unrelated state (e.g. `loading`) updates.
  const Row = useMemo(
    () =>
      React.memo(function Row({ index, style }) {
        const item = items[index];
        if (!item) {
          // Show loading skeleton for unmounted rows
          return (
            <div style={style} className="product-row product-row-skeleton">
              <div className="skeleton skeleton-text" style={{ width: '60%' }} />
              <div className="skeleton skeleton-text" style={{ width: '20%' }} />
            </div>
          );
        }
        return (
          <div style={style} className="product-row">
            <span>{item.name}</span>
            <span>${item.price.toFixed(2)}</span>
          </div>
        );
      }),
    [items]
  );

  return (
    <div className="product-list-container">
      <div className="search-box">
        <input
          type="text"
          placeholder="Search products..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          aria-label="Search products"
        />
      </div>

      {error && (
        <div className="error-message" role="alert">
          {error}
        </div>
      )}

      {loading && items.length === 0 && (
        <div className="loading-state">
          <div className="spinner" />
          <p>Loading products...</p>
        </div>
      )}

      {!loading && !error && items.length === 0 && totalCount === 0 && (
        <div className="empty-state">
          <p>No products found. Try a different search term.</p>
        </div>
      )}

      {totalCount > 0 && (
        <>
          {/* react-window only mounts DOM nodes for the rows currently visible in
              the viewport, so rendering cost stays constant whether the list has
              1,000 or 1,000,000 items. InfiniteLoader calls loadMoreItems as the
              user scrolls near the edge of what's been fetched. */}
          <InfiniteLoader isItemLoaded={isItemLoaded} itemCount={totalCount || 1} loadMoreItems={loadMoreItems}>
            {({ onItemsRendered, ref }) => (
              <List
                height={600}
                width="100%"
                itemCount={totalCount || 1}
                itemSize={ITEM_HEIGHT}
                onItemsRendered={onItemsRendered}
                ref={ref}
              >
                {Row}
              </List>
            )}
          </InfiniteLoader>
          <div className="product-count">
            Showing {Math.min(items.filter(Boolean).length, totalCount)} of {totalCount} products
          </div>
        </>
      )}
    </div>
  );
}
