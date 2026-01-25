from src.db.repository.budget_repository import BudgetRepository
from sqlmodel import Session

class BudgetService:
    def __init__(self):
        self._repository = BudgetRepository()

    def get_budget_by_user(self, user_id: int):
        # Implement the logic to retrieve budget data by user ID
        # Example:
        # budget_data = self._repository.get_budget_by_user_id(user_id)
        # return budget_data
        pass  # Replace with actual implementation

    def create_budget(self, session: Session, budget_data: dict):
        # Implement the logic to create a new budget for the specified user
        # Example:
        # new_budget = self._repository.create_budget(user_id, budget_data)
        # return new_budget
        return self._repository.create_budget(session, budget_data)