# ShopMind Frontend (V1 minimal)

Vanilla HTML/CSS/JS SPA that consumes the ShopMind REST API.

## Run

```bash
cd frontend
# any static server, e.g:
npx serve .
# or
python -m http.server 3000
```

Open http://localhost:3000, set API base to `http://localhost:5000` (or `http://localhost:5000` when API runs via Docker on 5000).

## Features

- Product listing + search/filter/sort/pagination
- Product detail
- Login/Register (JWT in localStorage)
- Cart (add/update/remove/clear)
- Checkout with fake payment (+ simulate failure/timeout)
- Order history + cancel
- Admin dashboard (products/categories/inventory/orders)

Seeded accounts (after API migration+seed):
- `admin@shopmind.local` / `Admin123!`
- `customer@shopmind.local` / `Customer123!`
