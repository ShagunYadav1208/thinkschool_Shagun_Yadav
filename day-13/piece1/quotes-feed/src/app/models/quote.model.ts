// Matches thinkschool_Shagun_Yadav/day-1/piece3/QuotesApi/Models/Quote.cs exactly -
// verified live against the running API (`GET /api/quotes/` returns
// `[{"id":1,"author":"...","text":"..."}]`), not assumed. There is no
// createdAt field on this API, unlike several sibling Quotes APIs elsewhere
// in this repo - the first draft of this file wrongly included one.
export interface Quote {
  id: number;
  author: string;
  text: string;
}
