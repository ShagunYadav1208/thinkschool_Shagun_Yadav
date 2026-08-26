import { Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { firstValueFrom } from 'rxjs';
import { QuotesService } from '../quotes.service';
import { Quote } from '../models/quote.model';

const LIMITS = { author: 100, text: 1000 } as const;

@Component({
  selector: 'app-create-quote-form-signal',
  standalone: true,
  imports: [FormField],
  templateUrl: './create-quote-form-signal.html',
  styleUrl: './create-quote-form-signal.css',
})
export class CreateQuoteFormSignal {
  private readonly quotesService = inject(QuotesService);

  @Output() created = new EventEmitter<Quote>();

  protected readonly limits = LIMITS;
  protected readonly model = signal({ author: '', text: '' });

  // Limits match CreateQuoteRequest.cs exactly, same as the reactive-forms version.
  protected readonly quoteForm = form(this.model, (p) => {
    required(p.author, { message: 'Author is required.' });
    maxLength(p.author, LIMITS.author, {
      message: `Author must be ${LIMITS.author} characters or fewer.`,
    });
    required(p.text, { message: 'Text is required.' });
    maxLength(p.text, LIMITS.text, {
      message: `Text must be ${LIMITS.text} characters or fewer.`,
    });
  });

  protected readonly serverError = signal<string | null>(null);

  protected async onSubmit(): Promise<void> {
    this.serverError.set(null);
    let hadServerFieldError = false;

    await submit(this.quoteForm, {
      action: async (field) => {
        try {
          const quote = await firstValueFrom(this.quotesService.createQuote(field().value()));
          this.model.set({ author: '', text: '' });
          this.created.emit(quote);
          return undefined;
        } catch (err: any) {
          if (err.status === 400 && err.error?.errors) {
            hadServerFieldError = true;
            const errors = err.error.errors as Record<string, string[]>;
            return Object.entries(errors).map(([fieldName, messages]) => ({
              fieldTree: (this.quoteForm as any)[fieldName],
              kind: 'server',
              message: messages[0],
            }));
          }
          this.serverError.set("Couldn't add the quote. Please try again.");
          return undefined;
        }
      },
      // The first draft omitted onInvalid entirely, on the wrong assumption
      // that submit() would mark fields touched and move focus the same way
      // reactive forms' markAllAsTouched() + a manual focus call did. It
      // doesn't: submit()'s action only runs once the form is already valid,
      // and it silently no-ops otherwise unless onInvalid is wired up -
      // confirmed missing with a real Playwright check (clicking submit on
      // an empty form showed no error at all and never touched a field).
      // markAsTouched() and focusBoundControl() are the Signal Forms
      // equivalents of reactive forms' markAllAsTouched() + @ViewChild.focus().
      onInvalid: () => {
        this.quoteForm().markAsTouched();
        if (this.quoteForm.author().invalid()) {
          this.quoteForm.author().focusBoundControl();
        } else if (this.quoteForm.text().invalid()) {
          this.quoteForm.text().focusBoundControl();
        }
      },
    });

    // A submission error targeting a field (the 400-from-the-real-API path
    // above) doesn't run onInvalid and doesn't move focus on its own either
    // - checked here, after submit() has fully settled, rather than inside
    // the action's catch block (a queueMicrotask there raced the framework's
    // own error-application, the same zoneless-timing trap hit earlier in
    // the reactive-forms version). Gated on hadServerFieldError so a
    // successful submit - which resets the model to '', making both
    // required validators invalid again - doesn't steal focus right back.
    if (hadServerFieldError) {
      if (this.quoteForm.author().invalid()) {
        this.quoteForm.author().focusBoundControl();
      } else if (this.quoteForm.text().invalid()) {
        this.quoteForm.text().focusBoundControl();
      }
    }
  }
}
