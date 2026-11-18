import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoansPage } from './loans';

class LoanHarness extends LoansPage {
  submit(): void { this.createLoan(); }
  setForm(bookId = 'book-1'): void {
    this.form = { bookId, readerId: 'reader-1', dueDate: '2099-12-31' };
  }
  setPastDate(): void { this.form.dueDate = '2000-01-01'; }
}

describe('Loan submission', () => {
  let page: LoanHarness;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    page = TestBed.runInInjectionContext(() => new LoanHarness());
    http.expectOne('/api/loans?limit=500').flush([]);
    http.expectOne('/api/books').flush([]);
    http.expectOne('/api/users').flush([]);
    page.setForm();
  });

  afterEach(() => http.verify());

  it('reuses the idempotency key after an uncertain network failure', () => {
    page.submit();
    const first = http.expectOne('/api/loans');
    const key = first.request.headers.get('Idempotency-Key');
    expect(key).toBeTruthy();
    first.error(new ProgressEvent('error'));

    page.submit();
    const retry = http.expectOne('/api/loans');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toEqual(first.request.body);
    retry.error(new ProgressEvent('error'));
  });

  it('uses a new key when the user changes the request', () => {
    page.submit();
    const first = http.expectOne('/api/loans');
    first.error(new ProgressEvent('error'));
    page.setForm('book-2');
    page.submit();
    const changed = http.expectOne('/api/loans');
    expect(changed.request.headers.get('Idempotency-Key')).not.toBe(first.request.headers.get('Idempotency-Key'));
    expect(changed.request.body.bookId).toBe('book-2');
    changed.error(new ProgressEvent('error'));
  });

  it('allows only one request while a submission is pending', () => {
    page.submit();
    page.submit();
    const pending = http.expectOne('/api/loans');
    pending.error(new ProgressEvent('error'));
  });

  it('rejects a past due date before calling the API', () => {
    page.setPastDate();
    page.submit();
    http.expectNone('/api/loans');
  });

  it('reloads loans and availability after a successful submission', () => {
    page.submit();
    http.expectOne('/api/loans').flush({ id: 'loan-1' });
    http.expectOne('/api/loans?limit=500').flush([]);
    http.expectOne('/api/books').flush([]);
    http.expectOne('/api/users').flush([]);
  });
});
