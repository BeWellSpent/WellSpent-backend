from db.repository.budget_repository import BudgetRepository
from sqlmodel import Session

class BudgetService:
    def __init__(self):
        self._repository = BudgetRepository()

    def get_all(self, session: Session):
        return self._repository.get_all(session)

    def get_budget_by_id(self, session: Session, budget_id: str):
        return self._repository.get_budget_by_id(session, budget_id)

    def get_budget_by_user(self, user_id: int, session: Session):
        return self._repository.get_budget_by_user_id(session, user_id)

    def create_budget(self, session: Session, budget_data: dict):
        # Implement the logic to create a new budget for the specified user
        # Example:
        # new_budget = self._repository.create_budget(user_id, budget_data)
        # return new_budget
        return self._repository.create_budget(session, budget_data)
    
    def add_people_to_budget(self, session: Session, budget_id: str, people: list[Person]):
        # Implement the logic to add people to a budget
        return self._repository.add_people_to_budget(session, budget_id, people)